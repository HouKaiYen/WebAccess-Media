using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace WebAccess.Media;

internal static class WindowsTrayLauncher
{
    public static void Start(WebApplication app)
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
            ProcessStartInfo start = new(executable) { UseShellExecute = false };
            start.ArgumentList.Add(localAddress.AbsoluteUri);
            using Process? process = Process.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            app.Logger.LogWarning(error, "無法啟動 Windows 通知區圖示。");
        }
    }

    private static Uri? AsLoopback(string value)
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
