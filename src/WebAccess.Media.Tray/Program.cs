using System.Diagnostics;
using System.Net.Http.Json;

namespace WebAccess.Media.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--configure-startup")
        {
            Environment.ExitCode = StartupSetup.Configure(args);
            return;
        }
        if (args.Length == 3 && args[0] == "--watch-startup" && TryReadAddress(args[1], out Uri? watched) &&
            args[2].Length == 16 && args[2].All(Uri.IsHexDigit))
        {
            using Mutex instance = new(false, $@"Local\WebAccess.Media.StartupTray.{args[2]}", out bool created);
            if (created) Run(watched!, null, null, args[2]);
            return;
        }
        if (args.Length != 3 || !TryReadAddress(args[0], out Uri? address) ||
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
                Run(address!, service, shutdown, null);
            }
        }
    }

    private static bool TryReadAddress(string value, out Uri? address) =>
        Uri.TryCreate(value, UriKind.Absolute, out address) && address.IsLoopback && address.Scheme is "http" or "https";

    private static void Run(Uri address, Process? service, EventWaitHandle? shutdown, string? startupId)
    {
        ApplicationConfiguration.Initialize();
        using ApplicationContext context = new();
        using ContextMenuStrip menu = new();
        string portLabel = $"Port {address.Port}";
        string tooltipPrefix = $"WebAccess Media · {portLabel}";
        ToolStripMenuItem port = new(portLabel) { Enabled = false };
        ToolStripMenuItem status = new("正在檢查服務…") { Enabled = false };
        ToolStripMenuItem open = new("開啟日常管理") { Enabled = startupId is null };
        ToolStripMenuItem exit = new("結束 WebAccess Media") { Enabled = startupId is null };
        ToolStripMenuItem closeIcon = new("關閉通知區圖示") { Visible = startupId is not null };
        menu.Items.AddRange([port, status, open, new ToolStripSeparator(), exit, closeIcon]);
        using Stream iconStream = typeof(Program).Assembly.GetManifestResourceStream("WebAccess.Media.Tray.AppIcon")!;
        using Icon applicationIcon = new(iconStream, SystemInformation.SmallIconSize);
        using NotifyIcon icon = new()
        {
            Icon = applicationIcon,
            Text = $"{tooltipPrefix} · {status.Text}",
            ContextMenuStrip = menu,
            Visible = true
        };
        using HttpClient client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(2)
        };
        using System.Windows.Forms.Timer timer = new() { Interval = 3000 };
        bool isStopping = false;
        void ShowStatus(string text)
        {
            status.Text = text;
            icon.Text = $"{tooltipPrefix} · {text}";
        }
        void CloseIcon()
        {
            icon.Visible = false;
            context.ExitThread();
        }
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (service?.HasExited == true)
            {
                CloseIcon();
                return;
            }
            try
            {
                ServiceStatus? result = await ReadStatusAsync(client);
                bool available = result?.Application == "WebAccess.Media" &&
                    (startupId is null || result.StartupInstance == startupId);
                if (isStopping && startupId is not null && !available)
                {
                    CloseIcon();
                }
                else if (!isStopping)
                {
                    open.Enabled = available;
                    exit.Enabled = available;
                    ShowStatus(available ? DescribeStatus(result!.ConnectionStatus) : "服務未執行");
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
        closeIcon.Click += (_, _) => CloseIcon();
        icon.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left && open.Enabled)
            {
                OpenSettings(address);
            }
        };
        exit.Click += (_, _) =>
        {
            bool signaled = shutdown?.Set() ?? SignalStartupShutdown(startupId!);
            if (!signaled)
            {
                ShowStatus("無法聯絡背景服務");
                return;
            }
            isStopping = true;
            exit.Enabled = false;
            open.Enabled = false;
            ShowStatus("正在結束服務…");
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

    private static bool SignalStartupShutdown(string startupId)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting($@"Global\WebAccess.Media.Startup.{startupId}", out EventWaitHandle? signal)) return false;
            using (signal) return signal.Set();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<ServiceStatus?> ReadStatusAsync(HttpClient client)
    {
        try
        {
            return await client.GetFromJsonAsync<ServiceStatus>("/api/status");
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string DescribeStatus(string connectionStatus) => connectionStatus switch
    {
        "connected" => "服務執行中 · WebAccess 已連線",
        "disconnected" => "服務執行中 · WebAccess 斷線",
        "connecting" => "服務執行中 · WebAccess 連線中",
        "unconfigured" => "服務執行中 · WebAccess 未設定",
        _ => "服務執行中"
    };

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

    private sealed record ServiceStatus(string Application, string ConnectionStatus, string? StartupInstance);
}
