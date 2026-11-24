using System.Net;
using Microsoft.AspNetCore.DataProtection;
using WebAccess.Media;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string settingsDirectory = builder.Configuration["Storage:Directory"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebAccess-Media");
IDataProtectionBuilder protection = builder.Services.AddDataProtection()
    .SetApplicationName("WebAccess-Media")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settingsDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    protection.ProtectKeysWithDpapi();
}

builder.Services.AddRazorPages();
builder.Services.AddHttpClient("WebAccess", client => client.Timeout = TimeSpan.FromSeconds(5))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(services => new WebAccessClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("WebAccess"),
    services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(services => new EngineeringSettings(
    settingsDirectory,
    services.GetRequiredService<IDataProtectionProvider>().CreateProtector("WebAccess.Connection.v1"),
    services.GetRequiredService<WebAccessClient>()));
builder.Services.AddSingleton<WebAccessAlarmMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<WebAccessAlarmMonitor>());

WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/engineering", StringComparison.OrdinalIgnoreCase))
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
app.MapGet("/", () => Results.Redirect("/display"));
app.MapGet("/display", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "Display.html"), "text/html; charset=utf-8"));
app.MapGet("/api/display", (WebAccessAlarmMonitor monitor) => Results.Ok(monitor.State));
app.Run();
