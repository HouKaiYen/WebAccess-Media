using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class AlarmAppearanceSteps : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private PointPlan? _plan;
    private string? _file;
    private Alarm? _alarm;

    [Given("載入沒有圖示欄位的測點設定")]
    public void GivenLegacyPlan() =>
        _plan = JsonSerializer.Deserialize<PointPlan>("""{"Point":"A","File":"a.png","X":25,"Y":75}""");

    [Then("預設顯示閃爍圓點且純圖片模式關閉")]
    public void ThenDefaults()
    {
        Assert.That(_plan?.Icon, Is.EqualTo("dot"));
        Assert.That(_plan?.Flash, Is.True);
        Assert.That(_plan?.ImageOnly, Is.False);
        Assert.That(_plan?.AlarmTextSize, Is.EqualTo(PointPlan.DefaultAlarmTextSize));
        Assert.That(_plan?.IconSize, Is.EqualTo(PointPlan.DefaultIconSize));
    }

    [When("上傳工程平面圖")]
    public async Task WhenUploadFloorPlan()
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jr9sAAAAASUVORK5CYII=");
        using MemoryStream input = new(png);
        IFormFile image = new FormFile(input, 0, input.Length, "images", "floor.png");
        _file = await new PlayerSettings(_directory.FullName).UploadEngineeringAsync(image, CancellationToken.None);
    }

    [Then("工程圖庫可讀取圖片且日常管理找不到該圖片")]
    public void ThenOnlyEngineeringCanRead()
    {
        PlayerSettings storage = new(_directory.FullName);
        Assert.That(storage.FindEngineeringMedia(_file!), Is.Not.Null);
        Assert.That(storage.FindMedia(_file!), Is.Null);
    }

    [Given("測點 A 設定自訂文字「(.*)」與指定尺寸")]
    public void GivenCustomAlarmText(string text) =>
        _plan = new PointPlan("A", "a.png", 25, 75, AlarmText: text, AlarmTextSize: 64, IconSize: 80);

    [When("測點 A 回報 API 警報文字「(.*)」")]
    public void WhenApiAlarm(string text) => _alarm = new Alarm("A", Description: text);

    [Then("顯示文字「(.*)」並沿用設定尺寸與位置")]
    public void ThenCustomAppearance(string text)
    {
        (Alarm? alarm, PointPlan? marker) = AlarmDisplay.SelectRegistered([_alarm!], [_plan!]);
        Assert.That(alarm?.Description, Is.EqualTo(text));
        Assert.That(marker, Is.EqualTo(_plan));
    }

    [When("清空測點 A 的自訂警報文字")]
    public void WhenClearText() => _plan = _plan! with { AlarmText = " " };

    [Then("顯示 API 警報文字「(.*)」")]
    public void ThenApiText(string text) =>
        Assert.That(AlarmDisplay.SelectRegistered([_alarm!], [_plan!]).Alarm?.Description, Is.EqualTo(text));

    public void Dispose() => _directory.Delete(recursive: true);
}
