using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

[RequestSizeLimit(102 * 1024 * 1024)]
public sealed class PointsModel(
    PlayerSettings storage, EngineeringSettings connection, WebAccessClient client, WebAccessAlarmMonitor monitor) : PageModel
{
    public static IReadOnlyList<(string Value, string Label)> AlarmIcons { get; } =
        [("sos", "SOS 圓牌"), ("triangle", "警告三角"), ("dot", "色彩圓點"), ("button", "緊急押鈕"), ("custom", "自訂圖示")];
    public PointPlan[] Plans { get; private set; } = [];
    public PointPlan? Selected { get; private set; }
    public string? CurrentAlarmText { get; private set; }
    public FloorImage[] FloorImages { get; private set; } = [];
    public int Revision { get; private set; }
    [TempData] public string? Feedback { get; set; }

    public async Task<IActionResult> OnGetTagsAsync()
    {
        try
        {
            if (connection.Current is not ConnectionSettings current)
            {
                return new JsonResult(new { error = "請先儲存 WebAccess 連線設定。" }) { StatusCode = 409 };
            }
            return new JsonResult(await client.GetTagsAsync(current, HttpContext.RequestAborted));
        }
        catch (Exception error) when (!HttpContext.RequestAborted.IsCancellationRequested &&
            (WebAccessClient.IsConnectionFailure(error) || error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException))
        {
            return new JsonResult(new { error = "無法讀取測點清單，仍可手動輸入測點名稱。" }) { StatusCode = 502 };
        }
    }

    public void OnGet(string? selected, bool add = false)
    {
        Playlist playlist = storage.Read();
        Plans = playlist.PointPlans ?? [];
        FloorImages = GetFloorImages(playlist);
        Revision = playlist.Revision;
        Selected = add ? null : Plans.FirstOrDefault(plan => plan.Point == selected) ?? Plans.FirstOrDefault();
        if (Selected is PointPlan plan)
        {
            CurrentAlarmText = monitor.State.Alarms.FirstOrDefault(alarm => alarm.Point == plan.Point)?.Description;
        }
    }

    public async Task<IActionResult> OnPostUploadFloorImagesAsync(List<IFormFile> images, int revision)
    {
        if (images.Count is < 1 or > 20 || images.Any(image => !IsFloorImage(image)))
        {
            return LibraryError("請選取 1–20 張 PNG 或 JPEG。", 400);
        }

        Playlist playlist = storage.Read();
        if (playlist.Revision != revision)
        {
            return LibraryError("設定已更新，請重新整理頁面後再上傳。", 409);
        }
        List<FloorImage> uploaded = [];
        bool saved = false;
        try
        {
            foreach (IFormFile image in images)
            {
                string file = await storage.UploadEngineeringAsync(image, HttpContext.RequestAborted);
                uploaded.Add(new FloorImage(file, Path.GetFileName(image.FileName)));
            }
            Playlist updated = playlist with { FloorImages = [.. (playlist.FloorImages ?? []), .. uploaded] };
            saved = storage.Save(updated);
            return saved ? LibraryResult(updated, $"已加入 {uploaded.Count} 張平面圖。")
                : LibraryError("設定已更新，請重新整理頁面後再上傳。", 409);
        }
        catch (InvalidDataException error)
        {
            return LibraryError(error.Message, 400);
        }
        finally
        {
            if (!saved)
            {
                foreach (FloorImage image in uploaded)
                {
                    storage.RemoveEngineeringMedia(image.File);
                }
            }
        }
    }

    public IActionResult OnPostDeleteFloorImage(string? file, int revision)
    {
        Playlist playlist = storage.Read();
        if (playlist.Revision != revision)
        {
            return LibraryError("設定已更新，請重新整理頁面後再刪除。", 409);
        }
        string[] usingPoints = (playlist.PointPlans ?? [])
            .Where(plan => plan.File == file || plan.IconFile == file)
            .Select(plan => plan.Point).ToArray();
        if (usingPoints.Length > 0)
        {
            return LibraryError($"這張圖片仍由測點 {string.Join("、", usingPoints)} 使用。請先更換圖片。", 409);
        }
        FloorImage[] images = playlist.FloorImages ?? [];
        FloorImage[] remaining = images.Where(image => image.File != file).ToArray();
        if (file is null || remaining.Length == images.Length)
        {
            return LibraryError("找不到這張共用平面圖。", 404);
        }
        Playlist updated = playlist with { FloorImages = remaining };
        if (!storage.Save(updated))
        {
            return LibraryError("設定已更新，請重新整理頁面後再刪除。", 409);
        }
        try
        {
            storage.RemoveEngineeringMedia(file);
        }
        catch (IOException)
        {
            return LibraryResult(updated, "已從圖庫移除，圖片檔案暫時無法刪除。");
        }
        catch (UnauthorizedAccessException)
        {
            return LibraryResult(updated, "已從圖庫移除，圖片檔案無法刪除。");
        }
        return LibraryResult(updated, "已刪除平面圖。");
    }

    private JsonResult LibraryResult(Playlist updated, string message) =>
        new(new { revision = updated.Revision + 1, images = GetFloorImages(updated), message });

    private static JsonResult LibraryError(string message, int statusCode) =>
        new(new { message }) { StatusCode = statusCode };

    public async Task<IActionResult> OnPostSaveAsync(
        string? point, int x, int y, IFormFile? image, string? sharedFile, int revision, bool isNew,
        string icon = "dot", bool flash = false, bool imageOnly = false, IFormFile? customIcon = null,
        string? alarmText = null, int alarmTextSize = PointPlan.DefaultAlarmTextSize, int iconSize = PointPlan.DefaultIconSize)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(point) || point.Length > 256 ||
            x is < 0 or > 100 || y is < 0 or > 100 ||
            (alarmText?.Length ?? 0) > 500 ||
            alarmTextSize is < PointPlan.MinAlarmTextSize or > PointPlan.MaxAlarmTextSize ||
            iconSize is < PointPlan.MinIconSize or > PointPlan.MaxIconSize ||
            !AlarmIcons.Any(option => option.Value == icon) ||
            (image is not null && !IsFloorImage(image)) || (customIcon is not null && !IsFloorImage(customIcon)))
        {
            Feedback = "請確認測點、警報文字、字級、座標與圖示大小，圖片限 PNG 或 JPEG。";
            return RedirectToPage();
        }

        Playlist playlist = storage.Read();
        string name = point.Trim();
        PointPlan? previous = playlist.PointPlans?.FirstOrDefault(plan => plan.Point == name);
        if (revision != playlist.Revision || (isNew && previous is not null))
        {
            Feedback = revision != playlist.Revision ? "設定已更新，請確認最新內容再儲存。" : "這個測點 Name 已存在。";
            return RedirectToPage(new { selected = name, add = isNew });
        }

        FloorImage? shared = GetFloorImages(playlist).FirstOrDefault(item => item.File == sharedFile);
        string? selectedFile = shared?.File ?? previous?.File;
        if (image is null && selectedFile is null)
        {
            Feedback = "新增測點時請選取共用平面圖或上傳一張圖片。";
            return RedirectToPage(new { add = true });
        }
        string? iconFile = previous?.IconFile;
        if (icon == "custom" && customIcon is null && iconFile is null)
        {
            Feedback = "請上傳 PNG 或 JPEG 自訂圖示。";
            return RedirectToPage(new { selected = name, add = isNew });
        }

        List<string> uploaded = [];
        bool saved = false;
        try
        {
            string file = selectedFile ?? "";
            FloorImage[] floorImages = playlist.FloorImages ?? [];
            if (image is not null)
            {
                file = await storage.UploadEngineeringAsync(image, HttpContext.RequestAborted);
                uploaded.Add(file);
                floorImages = [.. floorImages, new FloorImage(file, Path.GetFileName(image.FileName))];
            }
            if (icon == "custom" && customIcon is not null)
            {
                iconFile = await storage.UploadEngineeringAsync(customIcon, HttpContext.RequestAborted);
                uploaded.Add(iconFile);
            }
            PointPlan updated = new(name, file, x, y, icon, flash, imageOnly, iconFile,
                alarmText?.Trim(), alarmTextSize, iconSize);
            PointPlan[] plans = playlist.PointPlans ?? [];
            plans = previous is null ? [.. plans, updated] : plans.Select(plan => plan.Point == name ? updated : plan).ToArray();
            saved = storage.Save(playlist with { PointPlans = plans, FloorImages = floorImages });
            Feedback = saved ? "已儲存測點平面圖及告警圖示。" : "設定已更新，請重新上傳。";
        }
        catch (InvalidDataException error)
        {
            Feedback = error.Message;
        }
        finally
        {
            if (!saved)
            {
                foreach (string file in uploaded)
                {
                    storage.RemoveEngineeringMedia(file);
                }
            }
        }
        return RedirectToPage(new { selected = name });
    }

    private static bool IsFloorImage(IFormFile file) =>
        Path.GetExtension(file.FileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private static FloorImage[] GetFloorImages(Playlist playlist) =>
        (playlist.FloorImages ?? []).Concat((playlist.PointPlans ?? []).Select(plan => new FloorImage(plan.File, plan.Point)))
            .DistinctBy(image => image.File).ToArray();

    public IActionResult OnPostRemove(string point)
    {
        Playlist playlist = storage.Read();
        Feedback = storage.Save(playlist with
        {
            PointPlans = (playlist.PointPlans ?? []).Where(plan => plan.Point != point).ToArray()
        }) ? "已移除測點平面圖。" : "設定已更新，請再試一次。";
        return RedirectToPage();
    }
}
