using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

[RequestSizeLimit(102 * 1024 * 1024)]
public sealed class SettingsModel(PlayerSettings storage) : PageModel
{
    [BindProperty] public int Revision { get; set; }
    [BindProperty] public bool IsMuted { get; set; }
    [BindProperty] public bool MarqueeAtTop { get; set; }
    [BindProperty] public string? Marquee { get; set; }
    [BindProperty] public List<ItemInput> Items { get; set; } = [];
    [TempData] public string? Feedback { get; set; }

    public void OnGet()
    {
        Playlist playlist = storage.Read();
        Revision = playlist.Revision;
        IsMuted = playlist.IsMuted;
        MarqueeAtTop = playlist.MarqueeAtTop;
        Marquee = playlist.Marquee;
        Items = playlist.Items.Select(item => new ItemInput { File = item.File, Name = item.Name, Seconds = item.Seconds }).ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? media)
    {
        Playlist playlist = storage.Read();
        if (media is null || playlist.Items.Length >= 200)
        {
            Feedback = "請選取媒體，播放清單最多可加入 200 項。";
            return RedirectToPage();
        }

        try
        {
            string file = await storage.UploadAsync(media, HttpContext.RequestAborted);
            string name = Path.GetFileNameWithoutExtension(media.FileName);
            PlaylistItem item = new(file, name.Length <= 200 ? name : name[..200], 10);
            if (storage.Save(playlist with { Items = [.. playlist.Items, item] }))
            {
                Feedback = "已加入播放清單。";
            }
            else
            {
                storage.RemoveMedia(file);
                Feedback = "清單已更新，請重新上傳。";
            }
        }
        catch (InvalidDataException error)
        {
            Feedback = error.Message;
        }
        return RedirectToPage();
    }

    public IActionResult OnPostSave(string? remove)
    {
        Playlist playlist = storage.Read();
        HashSet<string> knownFiles = playlist.Items.Select(item => item.File).ToHashSet(StringComparer.Ordinal);
        if (playlist.Revision != Revision)
        {
            Feedback = "設定已更新，請檢查最新清單再編輯。";
        }
        else if (!ModelState.IsValid || (Marquee?.Length ?? 0) > 500 || Items.Count != knownFiles.Count ||
            !knownFiles.SetEquals(Items.Select(item => item.File)) || Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 || item.Seconds is < 3 or > 300))
        {
            Feedback = "播放清單資料無效。請確認名稱、停留秒數及跑馬燈長度。";
        }
        else
        {
            PlaylistItem[] items = Items.Where(item => item.File != remove)
                .Select(item => new PlaylistItem(item.File, item.Name.Trim(), item.Seconds)).ToArray();
            Feedback = storage.Save(playlist with
            {
                Items = items, IsMuted = IsMuted, Marquee = Marquee?.Trim() ?? "", MarqueeAtTop = MarqueeAtTop
            }) ? "已儲存待機設定。" : "設定已更新，請檢查最新清單再編輯。";
        }
        return RedirectToPage();
    }

    public sealed class ItemInput
    {
        public string File { get; set; } = "";
        public string Name { get; set; } = "";
        public int Seconds { get; set; }
    }
}
