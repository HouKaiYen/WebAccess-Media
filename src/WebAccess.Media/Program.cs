using WebAccess.Media;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("WebAccess", client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(services => new WebAccessAlarmMonitor(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("WebAccess"),
    services.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService(services => services.GetRequiredService<WebAccessAlarmMonitor>());
WebApplication app = builder.Build();
app.MapGet("/", () => Results.Redirect("/display"));
app.MapGet("/display", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "Display.html"), "text/html; charset=utf-8"));
app.MapGet("/api/display", (IConfiguration configuration, WebAccessAlarmMonitor monitor) =>
{
    if (!string.IsNullOrWhiteSpace(configuration["WebAccess:ApiBase"]))
    {
        return Results.Ok(monitor.State);
    }

    AlarmDisplay display = new();
    display.Update(configuration.GetSection("ActiveAlarms").Get<Alarm[]>() ?? []);
    return Results.Ok(new { alarm = display.Current });
});
app.Run();
