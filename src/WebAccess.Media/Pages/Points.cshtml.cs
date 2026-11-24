using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

public sealed class PointsModel(PlayerSettings storage) : PageModel
{
    public PointPlan[] Plans { get; private set; } = [];
    public PointPlan? Selected { get; private set; }
    [TempData] public string? Feedback { get; set; }

    public void OnGet(string? selected, bool add = false)
    {
        Plans = storage.Read().PointPlans ?? [];
        Selected = add ? null : Plans.FirstOrDefault(plan => plan.Point == selected) ?? Plans.FirstOrDefault();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? point, int x, int y, IFormFile? image)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(point) || point.Length > 256 ||
            x is < 0 or > 100 || y is < 0 or > 100 ||
            (image is not null && Path.GetExtension(image.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")))
        {
            Feedback = "請輸入測點 Name、0–100 的座標並選取 PNG 或 JPEG。";
            return RedirectToPage();
        }

        Playlist playlist = storage.Read();
        string name = point.Trim();
        PointPlan? previous = playlist.PointPlans?.FirstOrDefault(plan => plan.Point == name);
        if (image is null && previous is null)
        {
            Feedback = "新增測點時請選取平面圖。";
            return RedirectToPage(new { add = true });
        }
        try
        {
            string file = image is null ? previous!.File : await storage.UploadAsync(image, HttpContext.RequestAborted);
            PointPlan updated = new(name, file, x, y);
            PointPlan[] plans = playlist.PointPlans ?? [];
            plans = previous is null ? [.. plans, updated] : plans.Select(plan => plan.Point == name ? updated : plan).ToArray();
            if (storage.Save(playlist with { PointPlans = plans }))
            {
                Feedback = "已儲存測點平面圖。";
            }
            else
            {
                if (image is not null)
                {
                    storage.RemoveMedia(file);
                }
                Feedback = "設定已更新，請重新上傳。";
            }
        }
        catch (InvalidDataException error)
        {
            Feedback = error.Message;
        }
        return RedirectToPage(new { selected = name });
    }

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
