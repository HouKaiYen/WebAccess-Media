using System.Net;
using System.Text.Json;
using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class TagListSteps
{
    private string _response = "";
    private IReadOnlyList<WebAccessTag>? _tags;
    private JsonException? _error;

    [Given("WebAccess 以「(.*)」和「(.*)」回傳測點")]
    public void GivenTagList(string list, string description) =>
        _response = $$"""{"Result":{"Ret":0,"Total":1},"{{list}}":[{"Name":"Pump01","{{description}}":"消防泵"}]}""";

    [Given("WebAccess 回傳不完整的測點清單")]
    public void GivenPartialList() => _response = """{"Result":{"Ret":0,"Total":1},"Tags":[]}""";

    [When("工程設定查詢測點清單")]
    public async Task WhenFetchTags()
    {
        using HttpClient http = new(new TagHandler(_response));
        WebAccessClient client = new(http);
        try
        {
            _tags = await client.GetTagsAsync(new("http://192.0.2.1/WaWebService/Json", "Demo A", "demo", "mock"),
                CancellationToken.None);
        }
        catch (JsonException error)
        {
            _error = error;
        }
    }

    [Then("可選取測點 Pump01 且說明為「(.*)」")]
    public void ThenTagAvailable(string description) =>
        Assert.That(_tags, Is.EqualTo(new[] { new WebAccessTag("Pump01", description) }));

    [Then("不提供不完整的測點清單")]
    public void ThenNoTags()
    {
        Assert.That(_tags, Is.Null);
        Assert.That(_error, Is.Not.Null);
    }

    private sealed class TagHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri?.AbsolutePath, Is.EqualTo("/WaWebService/Json/TagList/Demo%20A"));
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Basic ZGVtbzptb2Nr"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
