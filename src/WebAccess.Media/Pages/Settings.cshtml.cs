using System.Globalization;
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
        Items = playlist.Items.Select(ItemInput.FromPlaylistItem).ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? media, int? revision)
    {
        Playlist playlist = storage.Read();
        bool wantsRow = Request.Headers["X-Playlist-Upload"] == "row";
        if ((wantsRow || revision.HasValue) && revision != playlist.Revision)
        {
            return UploadError("清單已在其他地方更新，請確認最新清單後再上傳。", 409, wantsRow);
        }
        if (media is null || playlist.Items.Length >= 200)
        {
            return UploadError("請選取媒體，播放清單最多可加入 200 項。", 400, wantsRow);
        }

        try
        {
            string file = await storage.UploadAsync(media, HttpContext.RequestAborted);
            string name = Path.GetFileNameWithoutExtension(media.FileName);
            if (string.IsNullOrWhiteSpace(name)) name = "未命名媒體";
            PlaylistItem item = new(file, name.Length <= 200 ? name : name[..200], 10);
            if (storage.Save(playlist with { Items = [.. playlist.Items, item] }))
            {
                if (wantsRow)
                {
                    Response.Headers["X-Playlist-Revision"] = (playlist.Revision + 1).ToString(CultureInfo.InvariantCulture);
                    return Partial("_PlaylistItemRow", (playlist.Items.Length, ItemInput.FromPlaylistItem(item)));
                }
                Feedback = "已加入播放清單。";
            }
            else
            {
                storage.RemoveMedia(file);
                return UploadError("清單已在其他地方更新，請確認最新清單後再上傳。", 409, wantsRow);
            }
        }
        catch (InvalidDataException error)
        {
            return UploadError(error.Message, 400, wantsRow);
        }
        return RedirectToPage();
    }

    private IActionResult UploadError(string message, int status, bool wantsRow)
    {
        if (wantsRow) return new JsonResult(new { message }) { StatusCode = status };
        Feedback = message;
        return RedirectToPage();
    }

    public IActionResult OnPostSave(string[]? remove)
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
            HashSet<string> removed = (remove ?? []).ToHashSet(StringComparer.Ordinal);
            PlaylistItem[] items = Items.Where(item => !removed.Contains(item.File))
                .Select(item => new PlaylistItem(item.File, item.Name.Trim(), item.Seconds)).ToArray();
            if (!storage.Save(playlist with
            {
                Items = items, IsMuted = IsMuted, Marquee = Marquee?.Trim() ?? "", MarqueeAtTop = MarqueeAtTop
            }))
            {
                Feedback = "設定已更新，請檢查最新清單再編輯。";
                return RedirectToPage();
            }

            Feedback = "已儲存待機設定。";
            foreach (string file in knownFiles.Where(removed.Contains))
            {
                try
                {
                    storage.RemoveUnusedMedia(file);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Feedback = "已儲存待機設定，但部分已移除的媒體檔案無法刪除。";
                }
            }
        }
        return RedirectToPage();
    }

    public sealed class ItemInput
    {
        public string File { get; set; } = "";
        public string Name { get; set; } = "";
        public int Seconds { get; set; }

        public static ItemInput FromPlaylistItem(PlaylistItem item) =>
            new() { File = item.File, Name = item.Name, Seconds = item.Seconds };
    }
}
