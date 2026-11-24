using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

public sealed class PointsModel(PlayerSettings storage) : PageModel
{
    public PointPlan[] Plans { get; private set; } = [];
    [TempData] public string? Feedback { get; set; }

    public void OnGet() => Plans = storage.Read().PointPlans ?? [];

    public async Task<IActionResult> OnPostSaveAsync(string? point, int x, int y, IFormFile? image)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(point) || point.Length > 256 || image is null ||
            x is < 0 or > 100 || y is < 0 or > 100 ||
            Path.GetExtension(image.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
        {
            Feedback = "請輸入測點 Name、0–100 的座標並選取 PNG 或 JPEG。";
            return RedirectToPage();
        }

        Playlist playlist = storage.Read();
        try
        {
            string file = await storage.UploadAsync(image, HttpContext.RequestAborted);
            string name = point.Trim();
            PointPlan[] plans = (playlist.PointPlans ?? []).Where(plan => plan.Point != name).ToArray();
            if (storage.Save(playlist with { PointPlans = [.. plans, new PointPlan(name, file, x, y)] }))
            {
                Feedback = "已儲存測點平面圖。";
            }
            else
            {
                storage.RemoveMedia(file);
                Feedback = "設定已更新，請重新上傳。";
            }
        }
        catch (InvalidDataException error)
        {
            Feedback = error.Message;
        }
        return RedirectToPage();
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
