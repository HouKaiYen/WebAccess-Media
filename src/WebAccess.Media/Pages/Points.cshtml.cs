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

    public async Task<IActionResult> OnGetTagsAsync(string? point)
    {
        try
        {
            if (point is not null && monitor.State.Alarms.FirstOrDefault(alarm => alarm.Point == point) is Alarm alarm &&
                !string.IsNullOrWhiteSpace(alarm.Description))
            {
                return new JsonResult(new WebAccessTag(point, alarm.Description));
            }
            if (connection.Current is not ConnectionSettings current)
            {
                return new JsonResult(new { error = "請先儲存 WebAccess 連線設定。" }) { StatusCode = 409 };
            }
            IReadOnlyList<WebAccessTag> tags = await client.GetTagsAsync(current, HttpContext.RequestAborted);
            return point is null ? new JsonResult(tags)
                : new JsonResult(tags.FirstOrDefault(tag => tag.Name == point) ?? new WebAccessTag(point, ""));
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
        if (string.IsNullOrEmpty(file))
        {
            return LibraryError("找不到這張共用平面圖。", 404);
        }
        Playlist playlist = storage.Read();
        if (playlist.Revision != revision)
        {
            return LibraryError("設定已更新，請重新整理頁面後再刪除。", 409);
        }
        string[] usingPoints = (playlist.PointPlans ?? [])
            .Where(plan => string.Equals(plan.File, file, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(plan.IconFile, file, StringComparison.OrdinalIgnoreCase))
            .Select(plan => plan.Point).ToArray();
        if (usingPoints.Length > 0)
        {
            return LibraryError($"這張圖片仍由測點 {string.Join("、", usingPoints)} 使用。請先更換圖片。", 409);
        }
        FloorImage[] images = playlist.FloorImages ?? [];
        FloorImage[] remaining = images.Where(image => image.File != file).ToArray();
        if (remaining.Length == images.Length)
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
            storage.RemoveUnusedMedia(file, isEngineeringMedia: true);
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
        string icon = PointPlan.DefaultIcon, bool flash = false, bool imageOnly = false, IFormFile? customIcon = null,
        string? alarmText = null, int alarmTextSize = PointPlan.DefaultAlarmTextSize, int iconSize = PointPlan.DefaultIconSize,
        string? suggestedAlarmText = null, bool addNext = false)
    {
        string? invalidField = ModelState.FirstOrDefault(entry => entry.Value?.Errors.Count > 0).Key;
        if (invalidField is not null)
        {
            return PointError("請確認此欄位的格式。", 400, point, isNew, invalidField);
        }
        if (string.IsNullOrWhiteSpace(point) || point.Length > 256)
        {
            return PointError("請輸入測點 Name，最多 256 個字元。", 400, point, isNew, "point");
        }
        if (x is < 0 or > 100 || y is < 0 or > 100)
        {
            return PointError("座標必須介於 0% 與 100%。", 400, point, isNew, x is < 0 or > 100 ? "x" : "y");
        }
        if ((alarmText?.Length ?? 0) > 500)
        {
            return PointError("警報文字最多 500 個字元。", 400, point, isNew, "alarmText");
        }
        if (alarmTextSize is < PointPlan.MinAlarmTextSize or > PointPlan.MaxAlarmTextSize)
        {
            return PointError($"文字大小必須介於 {PointPlan.MinAlarmTextSize} 與 {PointPlan.MaxAlarmTextSize} px。", 400, point, isNew, "alarmTextSize");
        }
        if (iconSize is < PointPlan.MinIconSize or > PointPlan.MaxIconSize)
        {
            return PointError($"圖示大小必須介於 {PointPlan.MinIconSize} 與 {PointPlan.MaxIconSize} px。", 400, point, isNew, "iconSize");
        }
        if (!AlarmIcons.Any(option => option.Value == icon))
        {
            return PointError("請選擇圖示樣式。", 400, point, isNew, "icon");
        }
        if (image is not null && !IsFloorImage(image))
        {
            return PointError("底圖限 PNG 或 JPEG。", 400, point, isNew, "image");
        }
        if (customIcon is not null && !IsFloorImage(customIcon))
        {
            return PointError("自訂圖示限 PNG 或 JPEG。", 400, point, isNew, "customIcon");
        }

        Playlist playlist = storage.Read();
        string name = point.Trim();
        PointPlan? previous = playlist.PointPlans?.FirstOrDefault(plan => plan.Point == name);
        if (revision != playlist.Revision)
        {
            return PointError("設定已在其他地方更新。請先確認最新內容。", 409, name, isNew);
        }
        if (isNew && previous is not null)
        {
            return PointError("這個測點 Name 已存在，請改用其他名稱。", 400, name, isNew, "point");
        }

        FloorImage? shared = GetFloorImages(playlist).FirstOrDefault(item => item.File == sharedFile);
        string? selectedFile = shared?.File ?? previous?.File;
        if (image is null && selectedFile is null)
        {
            return PointError("請選取共用平面圖或上傳一張圖片。", 400, name, isNew, "sharedFile");
        }
        string? iconFile = previous?.IconFile;
        if (icon == "custom" && customIcon is null && iconFile is null)
        {
            return PointError("請上傳 PNG 或 JPEG 自訂圖示。", 400, name, isNew, "customIcon");
        }

        List<string> uploaded = [];
        bool saved = false;
        string? uploadingField = null;
        try
        {
            string file = selectedFile ?? "";
            FloorImage[] floorImages = playlist.FloorImages ?? [];
            if (image is not null)
            {
                uploadingField = "image";
                file = await storage.UploadEngineeringAsync(image, HttpContext.RequestAborted);
                uploaded.Add(file);
                floorImages = [.. floorImages, new FloorImage(file, Path.GetFileName(image.FileName))];
            }
            if (icon == "custom" && customIcon is not null)
            {
                uploadingField = "customIcon";
                iconFile = await storage.UploadEngineeringAsync(customIcon, HttpContext.RequestAborted);
                uploaded.Add(iconFile);
            }
            PointPlan updated = new(name, file, x, y, icon, flash, imageOnly, iconFile,
                AlarmDisplay.ResolveTextOverride(alarmText, suggestedAlarmText), alarmTextSize, iconSize);
            PointPlan[] plans = playlist.PointPlans ?? [];
            plans = previous is null ? [.. plans, updated] : plans.Select(plan => plan.Point == name ? updated : plan).ToArray();
            saved = storage.Save(playlist with { PointPlans = plans, FloorImages = floorImages });
            if (!saved)
            {
                return PointError("設定已在其他地方更新。請先確認最新內容。", 409, name, isNew);
            }
            Feedback = "已儲存測點平面圖及告警圖示。";
        }
        catch (InvalidDataException error)
        {
            return PointError(error.Message, 400, name, isNew, uploadingField);
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
        object destination = isNew && addNext ? new { add = true } : new { selected = name };
        if (isNew && addNext)
        {
            Feedback = $"已儲存「{name}」，可以新增下一個測點。";
        }
        return WantsJson()
            ? new JsonResult(new { redirect = Url.Page("Points", destination) })
            : RedirectToPage(destination);
    }

    private bool WantsJson() => Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private IActionResult PointError(string message, int status, string? point, bool isNew, string? field = null)
    {
        if (WantsJson()) return new JsonResult(new { message, field }) { StatusCode = status };
        Feedback = message;
        return RedirectToPage(new { selected = point, add = isNew });
    }

    private static bool IsFloorImage(IFormFile file) =>
        Path.GetExtension(file.FileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private static FloorImage[] GetFloorImages(Playlist playlist) =>
        (playlist.FloorImages ?? []).Concat((playlist.PointPlans ?? []).Select(plan => new FloorImage(plan.File, plan.Point)))
            .DistinctBy(image => image.File).ToArray();

    public IActionResult OnPostRemove(string point, string? selected, bool add = false)
    {
        Playlist playlist = storage.Read();
        Feedback = storage.Save(playlist with
        {
            PointPlans = (playlist.PointPlans ?? []).Where(plan => plan.Point != point).ToArray()
        }) ? "已移除測點平面圖。" : "設定已更新，請再試一次。";
        return RedirectToPage(new { selected = selected == point ? null : selected, add });
    }
}
