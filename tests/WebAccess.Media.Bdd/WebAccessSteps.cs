using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class WebAccessSteps
{
    private readonly AlarmHandler _handler = new();
    private readonly WebAccessAlarmMonitor _monitor;

    public WebAccessSteps()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WebAccess:ApiBase"] = "http://example.invalid/api/",
                ["WebAccess:Project"] = "測試 A",
                ["WebAccess:Username"] = "demo",
                ["WebAccess:Password"] = "mock"
            }).Build();
        WebAccessClient client = new(new HttpClient(_handler));
        EngineeringSettings settings = new(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            new EphemeralDataProtectionProvider().CreateProtector("test"),
            client);
        _monitor = new WebAccessAlarmMonitor(client, settings, configuration);
    }

    [Given("WebAccess 已回報測點 A 的告警")]
    public async Task GivenAlarm()
    {
        await PollAsync("""{"Result":{"Ret":0,"Total":1},"AlarmTagList":[{"Name":"A","Desc":"測試告警","Time":"26/11/24 09:00:00"}]}""");
        Assert.That(_monitor.State.Alarms, Is.EqualTo(new[] { new Alarm("A", Description: "測試告警") }));
    }

    [When("WebAccess 回傳不完整清單")]
    public Task WhenIncomplete() => PollAsync("""{"Result":{"Ret":0,"Total":1},"AlarmTagList":[]}""");

    [Then("保留測點 A 並顯示連線異常")]
    public void ThenRetainsAlarm()
    {
        Assert.That(_monitor.State.Alarms.Single().Point, Is.EqualTo("A"));
        Assert.That(_monitor.State.LastSuccessAt, Is.Not.Null);
        Assert.That(_monitor.State.ConnectionStatus, Is.EqualTo("disconnected"));
    }

    [When("WebAccess 回傳完整空清單")]
    public Task WhenEmpty() => PollAsync("""{"Result":{"Ret":0,"Total":0},"AlarmTagList":[]}""");

    [Then("恢復待機並顯示已連線")]
    public void ThenIdle()
    {
        Assert.That(_monitor.State.Alarms, Is.Empty);
        Assert.That(_monitor.State.ConnectionStatus, Is.EqualTo("connected"));
    }

    private Task PollAsync(string response)
    {
        _handler.Response = response;
        return _monitor.PollAsync(CancellationToken.None);
    }

    private sealed class AlarmHandler : HttpMessageHandler
    {
        public string Response { get; set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri?.AbsolutePath, Is.EqualTo("/api/GetAlarmSummary/%E6%B8%AC%E8%A9%A6%20A/0/5000"));
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Basic ZGVtbzptb2Nr"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) });
        }
    }
}
