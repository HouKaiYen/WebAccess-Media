using WebAccess.Media;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();
app.MapGet("/", () => Results.Redirect("/display"));
app.MapGet("/display", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "Display.html"), "text/html; charset=utf-8"));
app.MapGet("/api/display", (IConfiguration configuration) =>
{
    AlarmDisplay display = new();
    display.Update(configuration.GetSection("ActiveAlarms").Get<Alarm[]>() ?? []);
    return Results.Ok(new { alarm = display.Current });
});
app.Run();
