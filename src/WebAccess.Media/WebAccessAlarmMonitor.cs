using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WebAccess.Media;

public sealed record WebAccessDisplayState(Alarm? Alarm, string ConnectionStatus, DateTimeOffset? LastSuccessAt = null);

public sealed class WebAccessAlarmMonitor(HttpClient client, IConfiguration configuration) : BackgroundService
{
    private volatile WebAccessDisplayState _state = new(null, "connecting");

    public WebAccessDisplayState State => _state;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["WebAccess:ApiBase"]))
        {
            _state = new(null, "unconfigured");
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? project = configuration["WebAccess:Project"];
            string? username = configuration["WebAccess:Username"];
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(username))
            {
                _state = _state with { ConnectionStatus = "unconfigured" };
                return;
            }

            string endpoint = $"{configuration["WebAccess:ApiBase"]?.TrimEnd('/')}/GetAlarmSummary/{Uri.EscapeDataString(project)}/0/5000";
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            string credentials = $"{username}:{configuration["WebAccess:Password"]}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            JsonElement root = document.RootElement;
            JsonElement result = root.GetProperty("Result");
            JsonElement rows = root.GetProperty("AlarmTagList");
            int total = result.GetProperty("Total").GetInt32();
            if (result.GetProperty("Ret").GetInt32() != 0 || total > 5000 || rows.GetArrayLength() != total)
            {
                throw new JsonException("WebAccess 告警清單不完整或查詢失敗。");
            }

            List<Alarm> alarms = [];
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string? point = row.GetProperty("Name").GetString();
                string? description = row.GetProperty("Desc").GetString();
                string? time = row.GetProperty("Time").GetString();
                if (string.IsNullOrWhiteSpace(point) || description is null || time is null)
                {
                    throw new JsonException("WebAccess 告警欄位無效。");
                }

                alarms.Add(new Alarm(point, configuration.GetSection("FloorPlans")[point], description));
            }

            AlarmDisplay display = new();
            display.Update(alarms);
            _state = new(display.Current, "connected", DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException
                or KeyNotFoundException or FormatException or ArgumentException)
        {
            _state = _state with { ConnectionStatus = "disconnected" };
        }
    }
}
