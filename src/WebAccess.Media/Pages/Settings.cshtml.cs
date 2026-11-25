using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

[RequestSizeLimit(102 * 1024 * 1024)]
public sealed class SettingsModel(PlayerSettings storage) : PageModel
{
    private const string HexColor = "^#[0-9a-fA-F]{6}$";
    [BindProperty] public int Revision { get; set; }
    [BindProperty] public bool IsMuted { get; set; }
    [BindProperty] public bool MarqueeAtTop { get; set; }
    [BindProperty] public string? Marquee { get; set; }
    [BindProperty] public bool? MarqueeEnabled { get; set; }
    [BindProperty, RegularExpression(HexColor)] public string? MarqueeTextColor { get; set; }
    [BindProperty, RegularExpression(HexColor)] public string? MarqueeBackgroundColor { get; set; }
    [BindProperty, Range(0, 100)] public int? MarqueeBackgroundOpacity { get; set; }
    [BindProperty] public int? MarqueeFontWeight { get; set; }
    [BindProperty] public string? MarqueeCharacterStyles { get; set; }
    [BindProperty, Range(Playlist.MinMarqueeFontSize, Playlist.MaxMarqueeFontSize)]
    public int? MarqueeFontSize { get; set; }
    [BindProperty, Range(Playlist.MinMarqueeSpeed, Playlist.MaxMarqueeSpeed)]
    public int? MarqueeSpeed { get; set; }
    [BindProperty] public List<ItemInput> Items { get; set; } = [];
    [TempData] public string? Feedback { get; set; }

    public void OnGet()
    {
        Playlist playlist = storage.Read();
        Revision = playlist.Revision;
        IsMuted = playlist.IsMuted;
        MarqueeAtTop = playlist.MarqueeAtTop;
        Marquee = playlist.Marquee;
        MarqueeEnabled = playlist.MarqueeEnabled;
        MarqueeTextColor = playlist.MarqueeTextColor;
        MarqueeBackgroundColor = playlist.MarqueeBackgroundColor;
        MarqueeBackgroundOpacity = playlist.MarqueeBackgroundOpacity;
        MarqueeFontWeight = playlist.MarqueeFontWeight;
        MarqueeCharacterStyles = JsonSerializer.Serialize(playlist.MarqueeCharacterStyles, JsonSerializerOptions.Web);
        MarqueeFontSize = playlist.MarqueeFontSize;
        MarqueeSpeed = playlist.MarqueeSpeed;
        Items = playlist.Items.Select(ItemInput.FromPlaylistItem).ToList();
    }

    public JsonResult OnGetMediaLibrary()
    {
        Playlist playlist = storage.Read();
        HashSet<string> filesInPlaylist = playlist.Items.Select(item => item.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new JsonResult(storage.GetStandbyMedia(playlist).Select(media => new
        {
            media.File, media.Name, IsInPlaylist = filesInPlaylist.Contains(media.File)
        }));
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

        string? file = null;
        bool registered = false;
        try
        {
            file = await storage.UploadAsync(media, HttpContext.RequestAborted);
            string name = Path.GetFileNameWithoutExtension(media.FileName);
            if (string.IsNullOrWhiteSpace(name)) name = "未命名媒體";
            PlaylistItem item = new(file, name.Length <= 200 ? name : name[..200], 10);
            registered = storage.Save(playlist with
            {
                Items = [.. playlist.Items, item],
                MediaLibrary = [.. (playlist.MediaLibrary ?? []), new StandbyMedia(file, item.Name)]
            });
            return registered ? MediaAdded(playlist, item, wantsRow)
                : UploadError("清單已在其他地方更新，請確認最新清單後再上傳。", 409, wantsRow);
        }
        catch (InvalidDataException error)
        {
            return UploadError(error.Message, 400, wantsRow);
        }
        finally
        {
            if (!registered && file is not null)
            {
                storage.RemoveMedia(file);
            }
        }
    }

    public IActionResult OnPostAddExisting(string? file, int revision)
    {
        Playlist playlist = storage.Read();
        if (revision != playlist.Revision)
        {
            return MediaError("清單已在其他地方更新，請重新整理後再加入。", 409);
        }
        StandbyMedia? media = storage.GetStandbyMedia(playlist).FirstOrDefault(item => item.File == file);
        if (media is null || storage.FindMedia(media.File) is null)
        {
            return MediaError("找不到這個媒體檔案。", 404);
        }
        if (playlist.Items.Any(item => string.Equals(item.File, media.File, StringComparison.OrdinalIgnoreCase)))
        {
            return MediaError("這個媒體已在播放清單中。", 409);
        }
        if (playlist.Items.Length >= 200)
        {
            return MediaError("播放清單最多可加入 200 項。", 400);
        }
        PlaylistItem item = new(media.File, media.Name, 10);
        return storage.Save(playlist with { Items = [.. playlist.Items, item] }) ? MediaAdded(playlist, item, true)
            : MediaError("清單已在其他地方更新，請重新整理後再加入。", 409);
    }

    public IActionResult OnPostDeleteMedia(string? file, int revision)
    {
        try
        {
            return storage.DeleteStandbyMedia(file, revision) switch
            {
                StandbyMediaDeletion.Deleted => new JsonResult(new { revision = revision + 1 }),
                StandbyMediaDeletion.CleanupPending => new JsonResult(new
                {
                    revision = revision + 1,
                    message = "已從現有媒體移除。程式下次啟動時會再清理本機檔案。"
                }),
                StandbyMediaDeletion.InUse => MediaError("檔案仍在使用中。請先從播放清單移除並儲存。", 409),
                StandbyMediaDeletion.NotFound => MediaError("找不到這個媒體檔案。", 404),
                _ => MediaError("清單已在其他地方更新，請重新整理後再刪除。", 409)
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return MediaError("無法完成刪除。請確認檔案使用狀態及寫入權限後重試。", 500);
        }
    }

    private IActionResult MediaAdded(Playlist playlist, PlaylistItem item, bool wantsRow)
    {
        if (wantsRow)
        {
            Response.Headers["X-Playlist-Revision"] = (playlist.Revision + 1).ToString(CultureInfo.InvariantCulture);
            return Partial("_PlaylistItemRow", (playlist.Items.Length, ItemInput.FromPlaylistItem(item)));
        }
        Feedback = "已加入播放清單。";
        return RedirectToPage();
    }

    private static JsonResult MediaError(string message, int status) =>
        new(new { message }) { StatusCode = status };

    private IActionResult UploadError(string message, int status, bool wantsRow)
    {
        if (wantsRow) return MediaError(message, status);
        Feedback = message;
        return RedirectToPage();
    }

    public IActionResult OnPostSave(string[]? remove)
    {
        Playlist playlist = storage.Read();
        string marquee = (Marquee ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        HashSet<string> knownFiles = playlist.Items.Select(item => item.File).ToHashSet(StringComparer.Ordinal);
        HashSet<string> removed = (remove ?? []).ToHashSet(StringComparer.Ordinal);
        HashSet<string> submittedFiles = Items.Select(item => item.File).ToHashSet(StringComparer.Ordinal);
        if (playlist.Revision != Revision)
        {
            Feedback = "設定已更新，請檢查最新清單再編輯。";
        }
        else if (ModelState[nameof(MarqueeFontSize)]?.Errors.Count > 0)
        {
            Feedback = $"跑馬燈文字大小須介於 {Playlist.MinMarqueeFontSize} 和 {Playlist.MaxMarqueeFontSize} 像素。";
        }
        else if (ModelState[nameof(MarqueeSpeed)]?.Errors.Count > 0)
        {
            Feedback = $"跑馬燈移動速度須介於 {Playlist.MinMarqueeSpeed} 和 {Playlist.MaxMarqueeSpeed} 像素／秒。";
        }
        else if (ModelState[nameof(MarqueeTextColor)]?.Errors.Count > 0 ||
            ModelState[nameof(MarqueeBackgroundColor)]?.Errors.Count > 0)
        {
            Feedback = "請用色彩選擇器設定跑馬燈的文字色和背景色。";
        }
        else if (ModelState[nameof(MarqueeBackgroundOpacity)]?.Errors.Count > 0)
        {
            Feedback = "跑馬燈背景不透明度須介於 0% 和 100%。";
        }
        else if (!IsMarqueeWeight(MarqueeFontWeight) ||
            ModelState[nameof(MarqueeFontWeight)]?.Errors.Count > 0)
        {
            Feedback = "請選擇有效的跑馬燈字重。";
        }
        else if (!ModelState.IsValid || marquee.Length > 500 || Items.Count != submittedFiles.Count ||
            !knownFiles.SetEquals(submittedFiles.Concat(removed)) || Items.Any(item => !removed.Contains(item.File) &&
                (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 || item.Seconds is < 3 or > 300)))
        {
            Feedback = "播放清單資料無效。請確認名稱、停留秒數及跑馬燈長度。";
        }
        else if (!TryReadCharacterStyles(playlist, marquee, out MarqueeCharacterStyle[] characterStyles))
        {
            Feedback = "無法儲存跑馬燈的逐字樣式，請確認字元、顏色和大小後再試。";
        }
        else
        {
            PlaylistItem[] items = Items.Where(item => !removed.Contains(item.File))
                .Select(item => new PlaylistItem(item.File, item.Name.Trim(), item.Seconds)).ToArray();
            if (!storage.Save(playlist with
            {
                Items = items, IsMuted = IsMuted, Marquee = marquee, MarqueeAtTop = MarqueeAtTop,
                MarqueeFontSize = MarqueeFontSize ?? playlist.MarqueeFontSize,
                MarqueeSpeed = MarqueeSpeed ?? playlist.MarqueeSpeed,
                MarqueeEnabled = MarqueeEnabled ?? playlist.MarqueeEnabled,
                MarqueeTextColor = MarqueeTextColor?.ToLowerInvariant() ?? playlist.MarqueeTextColor,
                MarqueeBackgroundColor = MarqueeBackgroundColor?.ToLowerInvariant() ?? playlist.MarqueeBackgroundColor,
                MarqueeBackgroundOpacity = MarqueeBackgroundOpacity ?? playlist.MarqueeBackgroundOpacity,
                MarqueeFontWeight = MarqueeFontWeight ?? playlist.MarqueeFontWeight,
                MarqueeCharacterStyles = characterStyles
            }))
            {
                Feedback = "設定已更新，請檢查最新清單再編輯。";
                return RedirectToPage();
            }

            Feedback = "已儲存待機設定。";
        }
        return RedirectToPage();
    }

    private static bool IsMarqueeWeight(int? value) => value is null or 400 or 600 or 700;

    private bool TryReadCharacterStyles(Playlist playlist, string text, out MarqueeCharacterStyle[] styles)
    {
        styles = [];
        if (MarqueeCharacterStyles is null)
        {
            styles = text == playlist.Marquee ? playlist.MarqueeCharacterStyles : [];
            return true;
        }
        if (MarqueeCharacterStyles.Length > 100_000) return false;

        try
        {
            MarqueeCharacterStyle[]? submitted = JsonSerializer.Deserialize<MarqueeCharacterStyle[]>(
                MarqueeCharacterStyles, JsonSerializerOptions.Web);
            if (submitted is null || submitted.Length > text.Length) return false;
            int[] boundaries = StringInfo.ParseCombiningCharacters(text);
            int previousEnd = 0;
            foreach (MarqueeCharacterStyle style in submitted)
            {
                if (style is null || style.Start < previousEnd || !IsMarqueeWeight(style.FontWeight) ||
                    style.FontSize is < Playlist.MinMarqueeFontSize or > Playlist.MaxMarqueeFontSize ||
                    (style.TextColor is not null && !Regex.IsMatch(style.TextColor, HexColor)))
                {
                    return false;
                }
                int index = Array.BinarySearch(boundaries, style.Start);
                if (index < 0) return false;
                int end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
                if (style.Length != end - style.Start) return false;
                previousEnd = end;
            }
            styles = submitted;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
