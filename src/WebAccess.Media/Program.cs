using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using WebAccess.Media;

string publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(publishedWebRoot) ? publishedWebRoot : null
});
bool isWindowsBundle = OperatingSystem.IsWindows() &&
    File.Exists(Path.Combine(AppContext.BaseDirectory, "tray", "WebAccess.Media.Tray.exe"));
string serverSettingsPath = Path.Combine(AppContext.BaseDirectory, "server-settings.json");
bool hasServerSettings = File.Exists(serverSettingsPath);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && (hasServerSettings || isWindowsBundle))
{
    int port = 3008;
    if (hasServerSettings)
    {
        using JsonDocument serverSettings = JsonDocument.Parse(File.ReadAllText(serverSettingsPath));
        if (serverSettings.RootElement.ValueKind != JsonValueKind.Object ||
            !serverSettings.RootElement.TryGetProperty("Port", out JsonElement configuredPort) ||
            configuredPort.ValueKind != JsonValueKind.Number ||
            !configuredPort.TryGetInt32(out port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("server-settings.json 的 Port 必須是 1 到 65535 之間的整數。");
        }
    }
    builder.Configuration["urls"] = $"http://127.0.0.1:{port}";
}
if (isWindowsBundle && Environment.UserInteractive && args.Length == 0)
{
    builder.Configuration["Tray:Enabled"] ??= "true";
}
string settingsDirectory = Path.GetFullPath(builder.Configuration["Storage:Directory"] ?? PlayerSettings.DefaultDirectory);
builder.Configuration["Storage:Directory"] = settingsDirectory;
Directory.CreateDirectory(settingsDirectory);
string leasePath = Path.Combine(settingsDirectory, ".service.lock");
FileStream? lease = OpenServiceLease(leasePath);
if (lease is null)
{
    string? address = ReadServiceAddress(leasePath);
    Console.Error.WriteLine(string.IsNullOrWhiteSpace(address)
        ? "已有 WebAccess Media 服務使用此資料夾。"
        : $"已有 WebAccess Media 服務使用此資料夾，監聽網址為 {address}。");
    Environment.ExitCode = 1;
    return;
}
using FileStream serviceLease = lease;
serviceLease.SetLength(0);
IDataProtectionBuilder protection = builder.Services.AddDataProtection()
    .SetApplicationName("WebAccess-Media")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settingsDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    protection.ProtectKeysWithDpapi();
}

builder.Services.AddRazorPages();
builder.Services.AddSingleton(new PlayerSettings(settingsDirectory));
builder.Services.AddHttpClient("WebAccess", client => client.Timeout = TimeSpan.FromSeconds(5))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(services => new WebAccessClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("WebAccess")));
builder.Services.AddSingleton(services => new EngineeringSettings(
    settingsDirectory,
    services.GetRequiredService<IDataProtectionProvider>().CreateProtector("WebAccess.Connection.v1"),
    services.GetRequiredService<WebAccessClient>()));
builder.Services.AddSingleton<WebAccessAlarmMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<WebAccessAlarmMonitor>());

WebApplication app = builder.Build();
app.Services.GetRequiredService<PlayerSettings>().RecoverStandbyMediaDeletes(app.Logger);
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        serviceLease.Write(System.Text.Encoding.UTF8.GetBytes(string.Join(", ", app.Urls)));
        serviceLease.Flush();
    }
    catch (IOException error)
    {
        app.Logger.LogWarning(error, "無法記錄目前的監聽網址。");
    }
});
string? startupInstance = null;
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/engineering", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/settings", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
    {
        IPAddress? remote = context.Connection.RemoteIpAddress;
        string host = context.Request.Host.Host.Trim('[', ']');
        bool localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));
        if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote) || !localHost)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XFrameOptions = "DENY";
    }
    await next(context);
});
app.MapRazorPages();
app.MapStaticAssets();
app.MapGet("/engineering/points", () => Results.Redirect("/engineering"));
app.MapGet("/", () => Results.Redirect("/display"));
app.MapGet("/display", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "Display.html"), "text/html; charset=utf-8"));
app.MapGet("/api/status", (WebAccessAlarmMonitor monitor) =>
    Results.Ok(new { Application = "WebAccess.Media", monitor.State.ConnectionStatus, StartupInstance = startupInstance }));
app.MapGet("/api/display", (WebAccessAlarmMonitor monitor, PlayerSettings player) =>
{
    WebAccessDisplayState state = monitor.State;
    Playlist playlist = player.Read();
    (Alarm? alarm, PointPlan? marker) = AlarmDisplay.SelectRegistered(state.Alarms, playlist.PointPlans ?? []);
    return Results.Ok(new
    {
        Alarm = alarm, Marker = marker is null ? null : new
        {
            marker.X, marker.Y, marker.Icon, marker.Flash, marker.ImageOnly, marker.AlarmTextSize, marker.IconSize,
            marker.AlarmSoundEnabled, marker.AlarmTone, marker.AlarmVolume,
            AlarmSoundUrl = marker.AlarmAudioFile is string audio ? $"/alarm-audio/{audio}" : null,
            IconUrl = marker.IconFile is string file ? $"/engineering-media/{file}" : null
        },
        state.ConnectionStatus, state.LastSuccessAt,
        Playlist = new
        {
            playlist.Revision, playlist.Items, playlist.IsMuted, playlist.Marquee,
            playlist.MarqueeAtTop, playlist.MarqueeFontSize, playlist.MarqueeSpeed,
            playlist.MarqueeEnabled, playlist.MarqueeTextColor, playlist.MarqueeBackgroundColor,
            playlist.MarqueeBackgroundOpacity, playlist.MarqueeFontWeight, playlist.MarqueeCharacterStyles
        }
    });
});
app.MapGet("/media/{file}", (string file, PlayerSettings player, HttpContext context) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return player.FindMedia(file) is string path
        ? Results.File(path, PlayerSettings.MimeType(Path.GetExtension(file)), enableRangeProcessing: true)
        : Results.NotFound();
});
app.MapGet("/engineering-media/{file}", (string file, PlayerSettings player, HttpContext context) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return player.FindEngineeringMedia(file) is string path
        ? Results.File(path, PlayerSettings.MimeType(Path.GetExtension(file)))
        : Results.NotFound();
});
app.MapGet("/alarm-audio/{file}", (string file, PlayerSettings player, HttpContext context) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return player.FindAlarmAudio(file) is string path
        ? Results.File(path, PlayerSettings.AlarmAudioMimeType(Path.GetExtension(file)), enableRangeProcessing: true)
        : Results.NotFound();
});
if (OperatingSystem.IsWindows())
{
    bool startsInBackground = app.Configuration.GetValue<bool>("Startup:Background");
    if (startsInBackground || app.Configuration.GetValue<bool>("Tray:Enabled"))
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            WindowsStartupTasks? background = startsInBackground
                ? WindowsTrayLauncher.GetStartupTasks(app.Urls, settingsDirectory) : null;
            startupInstance = background?.InstanceId;
            if (!startsInBackground || background is not null)
            {
                _ = WindowsTrayLauncher.RunAsync(app, background);
            }
        });
    }
}
app.Run();

static FileStream? OpenServiceLease(string path)
{
    try
    {
        FileShare share = OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, share);
    }
    catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33)
    {
        return null;
    }
}

static string? ReadServiceAddress(string path)
{
    try
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(file);
        return reader.ReadLine();
    }
    catch (IOException)
    {
        return null;
    }
}
