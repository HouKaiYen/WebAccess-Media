using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WebAccess.Media;

public sealed record WebAccessTag(string Name, string Description);

public sealed class WebAccessClient(HttpClient client)
{
    public async Task<IReadOnlyList<Alarm>> GetAlarmsAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        using JsonDocument document = await FetchAsync(settings, "GetAlarmSummary", "/0/5000", cancellationToken);
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

            alarms.Add(new Alarm(point, Description: description));
        }
        return alarms;
    }

    public async Task<IReadOnlyList<WebAccessTag>> GetTagsAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        using JsonDocument document = await FetchAsync(settings, "TagList", "", cancellationToken);
        JsonElement root = document.RootElement;
        JsonElement result = root.GetProperty("Result");
        if (!root.TryGetProperty("Tags", out JsonElement rows) || rows.ValueKind == JsonValueKind.Null)
        {
            rows = root.GetProperty("TagList");
        }
        if (result.GetProperty("Ret").GetInt32() != 0 || rows.ValueKind != JsonValueKind.Array ||
            (result.TryGetProperty("Total", out JsonElement total) &&
                (!total.TryGetInt32(out int count) || count != rows.GetArrayLength())))
        {
            throw new JsonException("WebAccess 測點清單不完整或查詢失敗。");
        }

        List<WebAccessTag> tags = [];
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string? name = row.GetProperty("Name").GetString();
            if (!row.TryGetProperty("Description", out JsonElement description) || description.ValueKind == JsonValueKind.Null)
            {
                description = row.GetProperty("Desc");
            }
            if (string.IsNullOrWhiteSpace(name) || description.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("WebAccess 測點欄位無效。");
            }
            tags.Add(new WebAccessTag(name, description.GetString()!));
        }
        return tags;
    }

    private async Task<JsonDocument> FetchAsync(
        ConnectionSettings settings, string resource, string suffix, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.ApiBase.Trim(), UriKind.Absolute, out Uri? apiBase) ||
            apiBase.Scheme is not ("http" or "https") || apiBase.UserInfo.Length > 0 ||
            apiBase.Query.Length > 0 || apiBase.Fragment.Length > 0 ||
            string.IsNullOrWhiteSpace(settings.Project) || settings.Project.Trim() is "." or ".." ||
            string.IsNullOrWhiteSpace(settings.Username) || settings.Username.Contains(':'))
        {
            throw new ArgumentException("請輸入有效的 API 基底網址、專案名稱及帳號。");
        }

        string endpoint = $"{apiBase.AbsoluteUri.TrimEnd('/')}/{resource}/{Uri.EscapeDataString(settings.Project.Trim())}{suffix}";
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        string credentials = $"{settings.Username.Trim()}:{settings.Password}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    public static bool IsConnectionFailure(Exception error) =>
        error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or ArgumentException;
}
