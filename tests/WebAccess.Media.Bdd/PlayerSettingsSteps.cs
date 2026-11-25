using NUnit.Framework;
using Reqnroll;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using WebAccess.Media;
using WebAccess.Media.Pages;

[Binding]
public sealed class PlayerSettingsSteps : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private string? _address;
    private readonly string _standbyFile = $"{Guid.NewGuid():N}.png";
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

    [Then("工程設定會依設定顯示預設位置或自訂資料夾")]
    public void ThenStorageLocationIsDisplayed()
    {
        PlayerSettings defaultStorage = new(PlayerSettings.DefaultDirectory);
        using HttpClient httpClient = new();
        WebAccessClient client = new(httpClient);
        EngineeringSettings connection = new(_directory.FullName,
            new EphemeralDataProtectionProvider().CreateProtector("test"), client);
        using WebAccessAlarmMonitor monitor = new(client, connection, new ConfigurationBuilder().Build());
        PointsModel defaultPage = new(defaultStorage, connection, client, monitor);
        PointsModel customPage = new(Open(), connection, client, monitor);
        string expectedDefault = OperatingSystem.IsWindows()
            ? @"%LOCALAPPDATA%\WebAccess-Media"
            : PlayerSettings.DefaultDirectory;
        Assert.That(defaultPage.StorageLocation, Is.EqualTo(expectedDefault));
        Assert.That(customPage.StorageLocation, Is.EqualTo(_directory.FullName));
    }

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

    [Then("舊公告仍啟用並保留原本的外觀")]
    public void ThenLegacyMarqueeAppearance()
    {
        Playlist playlist = Open().Read();
        Assert.Multiple(() =>
        {
            Assert.That(playlist.MarqueeEnabled, Is.True);
            Assert.That(playlist.MarqueeTextColor, Is.EqualTo("#ffffff"));
            Assert.That(playlist.MarqueeBackgroundColor, Is.EqualTo("#101923"));
            Assert.That(playlist.MarqueeFontWeight, Is.EqualTo(400));
            Assert.That(playlist.MarqueeBackgroundOpacity, Is.EqualTo(91));
            Assert.That(playlist.MarqueeCharacterStyles, Is.Empty);
        });
    }

    [When("管理員停用跑馬燈並儲存自訂外觀")]
    public void WhenMarqueeDisabled()
    {
        PlayerSettings storage = Open();
        Assert.That(storage.Save(storage.Read() with
        {
            MarqueeEnabled = false, MarqueeTextColor = "#ffdd66", MarqueeBackgroundColor = "#202040",
            MarqueeFontWeight = 700, MarqueeBackgroundOpacity = 65
        }), Is.True);
    }

    [Then("重載後跑馬燈仍停用且保留公告與自訂外觀")]
    public void ThenDisabledMarqueeSettingsPersist()
    {
        Playlist playlist = Open().Read();
        Assert.Multiple(() =>
        {
            Assert.That(playlist.MarqueeEnabled, Is.False);
            Assert.That(playlist.Marquee, Is.EqualTo("公告"));
            Assert.That(playlist.MarqueeTextColor, Is.EqualTo("#ffdd66"));
            Assert.That(playlist.MarqueeBackgroundColor, Is.EqualTo("#202040"));
            Assert.That(playlist.MarqueeFontWeight, Is.EqualTo(700));
            Assert.That(playlist.MarqueeBackgroundOpacity, Is.EqualTo(65));
        });
    }

    [When("管理員儲存含漢字和組合表情符號的公告逐字樣式")]
    public void WhenCharacterStylesSaved()
    {
        PlayerSettings storage = Open();
        Assert.That(storage.Save(storage.Read() with
        {
            Marquee = "好👩‍💻日",
            MarqueeCharacterStyles = [new(0, 1, "#ff4422", null, 700), new(1, 5, null, 44, null)]
        }), Is.True);
    }

    [Then("重載後兩個字元各自套用原本的樣式")]
    public void ThenCharacterStylesRemainSeparate()
    {
        Playlist playlist = Open().Read();
        Assert.That(playlist.MarqueeCharacterStyles, Is.EqualTo(new MarqueeCharacterStyle[]
        {
            new(0, 1, "#ff4422", null, 700), new(1, 5, null, 44, null)
        }));
        Assert.That(playlist.Marquee.Substring(1, 5), Is.EqualTo("👩‍💻"));
    }

    [Given("舊版播放清單引用一張已上傳的圖片")]
    public void GivenLegacyMedia()
    {
        string mediaDirectory = Path.Combine(_directory.FullName, "media");
        Directory.CreateDirectory(mediaDirectory);
        File.WriteAllText(Path.Combine(mediaDirectory, _standbyFile), "test");
        File.WriteAllText(Path.Combine(_directory.FullName, "playlist.json"),
            $$"""{"Revision":0,"Items":[{"File":"{{_standbyFile}}","Name":"首頁","Seconds":10}],"IsMuted":true,"Marquee":"","MarqueeAtTop":false}""");
    }

    [When("管理員從播放清單移除圖片並儲存")]
    public void WhenPlaylistItemRemoved()
    {
        PlayerSettings storage = Open();
        Assert.That(storage.Save(storage.Read() with { Items = [] }), Is.True);
    }

    [Then("現有媒體仍包含可讀取的圖片")]
    public void ThenMediaRemains()
    {
        PlayerSettings storage = Open();
        Playlist playlist = storage.Read();
        Assert.That(playlist.Items, Is.Empty);
        Assert.That(storage.GetStandbyMedia(playlist), Is.EqualTo(new[] { new StandbyMedia(_standbyFile, "首頁") }));
        Assert.That(storage.FindMedia(_standbyFile), Is.Not.Null);
    }

    [Then("仍在播放清單中的圖片不能從現有媒體刪除")]
    public void ThenMediaInUseCannotBeDeleted()
    {
        PlayerSettings storage = Open();
        Assert.That(storage.DeleteStandbyMedia(_standbyFile, storage.Read().Revision), Is.EqualTo(StandbyMediaDeletion.InUse));
        Assert.That(storage.FindMedia(_standbyFile), Is.Not.Null);
    }

    [When("管理員從現有媒體刪除圖片")]
    public void WhenMediaDeleted()
    {
        PlayerSettings storage = Open();
        Playlist playlist = storage.Read();
        Assert.That(storage.DeleteStandbyMedia(_standbyFile, playlist.Revision - 1), Is.EqualTo(StandbyMediaDeletion.Stale));
        Assert.That(storage.DeleteStandbyMedia(_standbyFile, playlist.Revision), Is.EqualTo(StandbyMediaDeletion.Deleted));
    }

    [Then("圖片及其本機副本已刪除")]
    public void ThenMediaAndLocalCopyAreGone()
    {
        PlayerSettings storage = Open();
        Assert.That(storage.GetStandbyMedia(storage.Read()), Is.Empty);
        Assert.That(storage.FindMedia(_standbyFile), Is.Null);
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
