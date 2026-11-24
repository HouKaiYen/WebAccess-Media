using System.Net;
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class EngineeringSettingsSteps : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private readonly StubWebAccess _stub = new();
    private readonly HttpClient _httpClient;
    private readonly ConnectionSettings _valid = new("http://example.invalid/api", "Project", "demo", "sample-password");
    private readonly EngineeringSettings _settings;

    public EngineeringSettingsSteps()
    {
        _httpClient = new HttpClient(_stub);
        _settings = OpenSettings();
    }

    [Given("工程師輸入有效的連線設定")]
    public void GivenValidSettings() => _stub.Status = HttpStatusCode.OK;

    [When("工程師測試連線")]
    public Task WhenTestConnection() => _settings.TestAsync(_valid, CancellationToken.None);

    [Then("測試成功且設定尚未儲存")]
    public void ThenNothingSaved() => Assert.That(_settings.Current, Is.Null);

    [Given("工程師已儲存可用的連線設定")]
    [When("工程師儲存設定")]
    public Task WhenSave() => _settings.SaveAsync(_valid, CancellationToken.None);

    [Then("重啟後載入相同設定且磁碟沒有明文密碼")]
    public void ThenReloadsSettings()
    {
        Assert.That(OpenSettings().Current, Is.EqualTo(_valid));
        Assert.That(File.ReadAllText(Path.Combine(_directory.FullName, "connection.protected")),
            Does.Not.Contain(_valid.Password));
    }

    [When("工程師用無法連線的資料嘗試儲存")]
    public void WhenSaveFails()
    {
        _stub.Status = HttpStatusCode.Unauthorized;
        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _settings.SaveAsync(_valid with { Username = "incorrect" }, CancellationToken.None));
    }

    [Then("拒絕變更且原設定仍可載入")]
    public void ThenOriginalSettingsRemain() => Assert.That(OpenSettings().Current, Is.EqualTo(_valid));

    private EngineeringSettings OpenSettings()
    {
        IDataProtectionProvider provider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_directory.FullName, "keys")), builder =>
            {
                if (OperatingSystem.IsWindows())
                {
                    builder.ProtectKeysWithDpapi();
                }
            });
        return new EngineeringSettings(_directory.FullName, provider.CreateProtector("test"),
            new WebAccessClient(_httpClient));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _directory.Delete(recursive: true);
    }

    private sealed class StubWebAccess : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("""{"Result":{"Ret":0,"Total":0},"AlarmTagList":[]}""")
            });
    }
}
