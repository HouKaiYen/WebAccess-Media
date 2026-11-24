using System.Diagnostics;
using System.Net.Http.Json;

namespace WebAccess.Media.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 3 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? address) ||
            !address.IsLoopback || address.Scheme is not ("http" or "https") ||
            !int.TryParse(args[1], out int processId) || processId <= 0 ||
            !EventWaitHandle.TryOpenExisting(args[2], out EventWaitHandle? shutdown))
        {
            return;
        }

        using (shutdown)
        using (Process? service = FindProcess(processId))
        {
            if (service is not null)
            {
                Run(address, service, shutdown);
            }
        }
    }

    private static void Run(Uri address, Process service, EventWaitHandle shutdown)
    {
        ApplicationConfiguration.Initialize();
        using ApplicationContext context = new();
        using ContextMenuStrip menu = new();
        ToolStripMenuItem status = new("正在檢查服務…") { Enabled = false };
        ToolStripMenuItem open = new("開啟日常管理");
        ToolStripMenuItem exit = new("結束 WebAccess Media");
        menu.Items.AddRange([status, open, new ToolStripSeparator(), exit]);
        using NotifyIcon icon = new()
        {
            Icon = SystemIcons.Application,
            Text = "WebAccess Media",
            ContextMenuStrip = menu,
            Visible = true
        };
        using HttpClient client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(2)
        };
        using System.Windows.Forms.Timer timer = new() { Interval = 3000 };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (service.HasExited)
            {
                icon.Visible = false;
                context.ExitThread();
                return;
            }
            try
            {
                if (exit.Enabled)
                {
                    string text = await ReadStatusAsync(client);
                    if (exit.Enabled)
                    {
                        status.Text = text;
                        icon.Text = $"WebAccess Media · {text}";
                    }
                }
            }
            finally
            {
                if (icon.Visible)
                {
                    timer.Start();
                }
            }
        };
        open.Click += (_, _) => OpenSettings(address);
        icon.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left && open.Enabled)
            {
                OpenSettings(address);
            }
        };
        exit.Click += (_, _) =>
        {
            exit.Enabled = false;
            open.Enabled = false;
            status.Text = "正在結束服務…";
            icon.Text = $"WebAccess Media · {status.Text}";
            shutdown.Set();
        };
        timer.Start();
        Application.Run(context);
    }

    private static Process? FindProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<string> ReadStatusAsync(HttpClient client)
    {
        try
        {
            ServiceStatus? result = await client.GetFromJsonAsync<ServiceStatus>("/api/status");
            if (result?.Application != "WebAccess.Media")
            {
                return "服務未執行";
            }
            return result.ConnectionStatus switch
            {
                "connected" => "服務執行中 · WebAccess 已連線",
                "disconnected" => "服務執行中 · WebAccess 斷線",
                "connecting" => "服務執行中 · WebAccess 連線中",
                "unconfigured" => "服務執行中 · WebAccess 未設定",
                _ => "服務執行中"
            };
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return "服務未執行";
        }
    }

    private static void OpenSettings(Uri address)
    {
        try
        {
            using Process? browser = Process.Start(new ProcessStartInfo(new Uri(address, "/settings").AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("無法開啟瀏覽器。請檢查 Windows 的預設瀏覽器設定。", "WebAccess Media",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record ServiceStatus(string Application, string ConnectionStatus);
}
