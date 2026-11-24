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

    [Given("載入沒有圖示欄位的測點設定")]
    public void GivenLegacyPlan() =>
        _plan = JsonSerializer.Deserialize<PointPlan>("""{"Point":"A","File":"a.png","X":25,"Y":75}""");

    [Then("預設顯示閃爍圓點且純圖片模式關閉")]
    public void ThenDefaults()
    {
        Assert.That(_plan?.Icon, Is.EqualTo("dot"));
        Assert.That(_plan?.Flash, Is.True);
        Assert.That(_plan?.ImageOnly, Is.False);
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

    public void Dispose() => _directory.Delete(recursive: true);
}
