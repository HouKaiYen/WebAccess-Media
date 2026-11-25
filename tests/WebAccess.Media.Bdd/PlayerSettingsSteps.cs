using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class PlayerSettingsSteps : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private string? _address;
    private PlayerSettings Open() => new(_directory.FullName);

    [Given("工程師輸入 IP「(.*)」")]
    public void GivenIp(string ip)
    {
        _address = WebAccessAddress.FromIp(ip);
        Assert.That(WebAccessAddress.FromIp("http://192.0.2.20/path"), Is.Null);
        Assert.That(WebAccessAddress.FromIp("192.0.2.20:8080"), Is.Null);
    }

    [Then("WebAccess 基底網址為「(.*)」")]
    public void ThenAddress(string address) => Assert.That(_address, Is.EqualTo(address));

    [Given("播放清單包含兩張圖片")]
    public void GivenTwoImages()
    {
        PlayerSettings storage = Open();
        Playlist playlist = storage.Read();
        Assert.That(storage.Save(playlist with
        {
            Items = [new PlaylistItem("a.png", "A", 10), new PlaylistItem("b.png", "B", 20)]
        }), Is.True);
    }

    [When("管理員將第二張圖片移到第一張並儲存")]
    public void WhenReordered()
    {
        PlayerSettings storage = Open();
        Playlist playlist = storage.Read();
        Assert.That(storage.Save(playlist with { Items = [playlist.Items[1], playlist.Items[0]] }), Is.True);
        Assert.That(storage.Save(playlist), Is.False);
    }

    [Then("重載後按照新的順序播放")]
    public void ThenOrderPersists() =>
        Assert.That(Open().Read().Items.Select(item => item.Name), Is.EqualTo(new[] { "B", "A" }));

    [Given("舊版待機設定將公告顯示在上方")]
    public void GivenLegacyMarquee() =>
        File.WriteAllText(Path.Combine(_directory.FullName, "playlist.json"),
            """{"Revision":0,"Items":[],"IsMuted":true,"Marquee":"公告","MarqueeAtTop":true}""");

    [When("^管理員將跑馬燈改為下方、([0-9]+) 像素和 ([0-9]+) 像素每秒並儲存$")]
    public void WhenMarqueeUpdated(int fontSize, int speed)
    {
        PlayerSettings storage = Open();
        Assert.That(storage.Save(storage.Read() with
        {
            MarqueeAtTop = false, MarqueeFontSize = fontSize, MarqueeSpeed = speed
        }), Is.True);
    }

    [Then("^重載後跑馬燈在(上|下)方，文字大小為 ([0-9]+) 像素，速度為 ([0-9]+) 像素每秒$")]
    public void ThenMarqueePersists(string position, int fontSize, int speed)
    {
        Playlist playlist = Open().Read();
        Assert.Multiple(() =>
        {
            Assert.That(playlist.Marquee, Is.EqualTo("公告"));
            Assert.That(playlist.MarqueeAtTop, Is.EqualTo(position == "上"));
            Assert.That(playlist.MarqueeFontSize, Is.EqualTo(fontSize));
            Assert.That(playlist.MarqueeSpeed, Is.EqualTo(speed));
        });
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
