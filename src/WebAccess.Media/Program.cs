using System.Net;
using Microsoft.AspNetCore.DataProtection;
using WebAccess.Media;

string publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(publishedWebRoot) ? publishedWebRoot : null
});
string settingsDirectory = builder.Configuration["Storage:Directory"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebAccess-Media");
builder.Configuration["Storage:Directory"] = Path.GetFullPath(settingsDirectory);
IDataProtectionBuilder protection = builder.Services.AddDataProtection()
    .SetApplicationName("WebAccess-Media")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settingsDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    protection.ProtectKeysWithDpapi();
}

builder.Services.AddRazorPages();
builder.Services.AddSingleton(new PlayerSettings(Path.GetFullPath(settingsDirectory)));
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
            IconUrl = marker.IconFile is string file ? $"/engineering-media/{file}" : null
        },
        state.ConnectionStatus, state.LastSuccessAt,
        Playlist = new
        {
            playlist.Revision, playlist.Items, playlist.IsMuted, playlist.Marquee,
            playlist.MarqueeAtTop, playlist.MarqueeFontSize, playlist.MarqueeSpeed
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
