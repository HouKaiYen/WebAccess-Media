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

    public void Dispose() => _directory.Delete(recursive: true);
}
