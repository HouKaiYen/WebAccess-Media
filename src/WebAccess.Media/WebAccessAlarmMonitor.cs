namespace WebAccess.Media;

public sealed record WebAccessDisplayState(Alarm? Alarm, string ConnectionStatus, DateTimeOffset? LastSuccessAt = null);

public sealed class WebAccessAlarmMonitor(
    WebAccessClient client, EngineeringSettings settings, IConfiguration configuration) : BackgroundService
{
    private volatile WebAccessDisplayState _state = new(null, "unconfigured");
    private ConnectionSettings? _lastConnection;

    public WebAccessDisplayState State => _state;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            ConnectionSettings? connection = settings.Current ?? GetStartupConnection();
            if (connection != _lastConnection)
            {
                _state = new(null, "connecting");
                _lastConnection = connection;
            }

            if (connection is null)
            {
                AlarmDisplay demo = new();
                demo.Update(configuration.GetSection("ActiveAlarms").Get<Alarm[]>() ?? []);
                _state = new(demo.Current, "unconfigured");
                return;
            }

            AlarmDisplay display = new();
            display.Update(await client.GetAlarmsAsync(connection, cancellationToken));
            _state = new(display.Current, "connected", DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            (WebAccessClient.IsConnectionFailure(error) || error is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException))
        {
            _state = _state with { ConnectionStatus = "disconnected" };
        }
    }

    private ConnectionSettings? GetStartupConnection()
    {
        string? apiBase = configuration["WebAccess:ApiBase"];
        return string.IsNullOrWhiteSpace(apiBase) ? null : new ConnectionSettings(
            apiBase, configuration["WebAccess:Project"] ?? "", configuration["WebAccess:Username"] ?? "",
            configuration["WebAccess:Password"] ?? "");
    }
}
