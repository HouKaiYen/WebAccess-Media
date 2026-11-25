using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

public sealed class StartupModel(IServer server, IConfiguration configuration, ILogger<StartupModel> logger) : PageModel
{
    public bool IsAvailable { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool NeedsUpdate { get; private set; }
    [TempData] public string? Feedback { get; set; }

    public void OnGet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            WindowsStartupTasks? tasks = WindowsTrayLauncher.GetStartupTasks(
                server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [], configuration["Storage:Directory"]!);
            if (tasks is not null && System.IO.File.Exists(tasks.TrayExecutable))
            {
                (IsEnabled, NeedsUpdate) = tasks.ReadState();
                IsAvailable = true;
            }
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "無法讀取 Windows 工作排程。");
            Feedback ??= "無法讀取 Windows 工作排程。";
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return RedirectToPage();
        }
        try
        {
            WindowsStartupTasks? tasks = WindowsTrayLauncher.GetStartupTasks(
                server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [], configuration["Storage:Directory"]!);
            if (tasks is null || !System.IO.File.Exists(tasks.TrayExecutable))
            {
                Feedback = "請在完整的 Windows 可攜套件中操作。";
            }
            else if (tasks.TrySetEnabled(enabled))
            {
                Feedback = enabled ? "已開啟開機自動啟動。" : "已關閉開機自動啟動。目前的監控會繼續執行。";
            }
            else if (!Environment.UserInteractive || configuration.GetValue<bool>("Startup:Background"))
            {
                Feedback = "建立或更新排程需要 Windows 桌面。請在本機桌面啟動可攜版後再設定。";
            }
            else
            {
                using Process? setup = Process.Start(tasks.CreateSetupProcess());
                if (setup is not null)
                {
                    await setup.WaitForExitAsync(HttpContext.RequestAborted);
                }
                Feedback = setup?.ExitCode switch
                {
                    WindowsStartupTasks.SetupSucceeded => "已開啟開機自動啟動。",
                    WindowsStartupTasks.SetupCancelled => "已取消 Windows 帳號驗證。",
                    _ => "無法建立開機排程。請確認 Windows 登入密碼與帳號的批次登入權限後再試。"
                };
            }
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            Feedback = "已取消 Windows 管理員授權。";
        }
        catch (Exception error) when (error is COMException or Win32Exception or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "無法變更開機自動啟動。");
            Feedback = "無法變更 Windows 排程。請確認這個帳號有權管理 WebAccess Media 的工作排程。";
        }
        return RedirectToPage();
    }
}
