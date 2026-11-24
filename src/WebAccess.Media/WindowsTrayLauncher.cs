using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace WebAccess.Media;

[SupportedOSPlatform("windows")]
internal static class WindowsTrayLauncher
{
    public static WindowsStartupTasks? GetStartupTasks(IEnumerable<string> addresses, string storageDirectory)
    {
        string[] urls = addresses.ToArray();
        Uri? localAddress = urls.Select(AsLoopback).FirstOrDefault(address => address is not null);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string executable = Path.Combine(AppContext.BaseDirectory, "WebAccess.Media.exe");
        return localAddress is not null && identity.User is SecurityIdentifier user
            ? new WindowsStartupTasks(executable, Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageDirectory)),
                string.Join(';', urls), localAddress, user.Value)
            : null;
    }

    public static async Task RunAsync(WebApplication app, WindowsStartupTasks? background)
    {
        Uri? localAddress = app.Urls.Select(AsLoopback).FirstOrDefault(address => address is not null);
        if (localAddress is null)
        {
            app.Logger.LogWarning("通知區圖示需要可從本機回送位址存取的管理頁。");
            return;
        }

        string executable = Path.Combine(AppContext.BaseDirectory, "tray", "WebAccess.Media.Tray.exe");
        try
        {
            string eventName = background?.ShutdownEvent ?? $@"Local\WebAccess.Media.Shutdown.{Guid.NewGuid():N}";
            using EventWaitHandle shutdown = new(false, EventResetMode.ManualReset, eventName);
            RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(shutdown,
                (_, _) => app.Lifetime.StopApplication(), null, Timeout.Infinite, executeOnlyOnce: true);
            try
            {
                if (background is null)
                {
                    ProcessStartInfo start = new(executable) { UseShellExecute = false };
                    start.ArgumentList.Add(localAddress.AbsoluteUri);
                    start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    start.ArgumentList.Add(eventName);
                    using Process? process = Process.Start(start);
                }
                await Task.Delay(Timeout.InfiniteTimeSpan, app.Lifetime.ApplicationStopped);
            }
            finally
            {
                wait.Unregister(null);
            }
        }
        catch (OperationCanceledException) when (app.Lifetime.ApplicationStopped.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            app.Logger.LogWarning(error, "無法啟動 Windows 通知區圖示。");
        }
    }

    internal static Uri? AsLoopback(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? address) || address.Scheme is not ("http" or "https"))
        {
            return null;
        }
        if (address.IsLoopback)
        {
            return address;
        }
        return address.Host switch
        {
            "0.0.0.0" => new UriBuilder(address) { Host = IPAddress.Loopback.ToString() }.Uri,
            "[::]" => new UriBuilder(address) { Host = IPAddress.IPv6Loopback.ToString() }.Uri,
            _ => null
        };
    }
}
