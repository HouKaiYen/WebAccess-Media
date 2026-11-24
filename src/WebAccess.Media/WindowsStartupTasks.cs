using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace WebAccess.Media;

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupTasks(string executable, string storage, string urls, Uri localAddress, string userSid)
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonPassword = 1;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskTriggerBoot = 8;
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;
    private const int FileNotFound = unchecked((int)0x80070002);

    public string InstanceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{executable.ToUpperInvariant()}|{userSid}|{localAddress.Port.ToString(CultureInfo.InvariantCulture)}")))[..16];
    public string ShutdownEvent => $@"Global\WebAccess.Media.Startup.{InstanceId}";
    public string TrayExecutable => Path.Combine(Path.GetDirectoryName(executable)!, "tray", "WebAccess.Media.Tray.exe");
    private string BootTask => $"WebAccess Media - 開機監控 ({InstanceId})";
    private string TrayTask => $"WebAccess Media - 登入通知區 ({InstanceId})";
    private string BootArguments => $"--urls {Quote(urls)} --Storage:Directory {Quote(storage)} --Startup:Background true";
    private string TrayArguments => $"--watch-startup \"{localAddress.AbsoluteUri}\" {InstanceId}";

    public ProcessStartInfo CreateSetupProcess()
    {
        ProcessStartInfo start = new(TrayExecutable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--configure-startup");
        start.ArgumentList.Add(storage);
        start.ArgumentList.Add(urls);
        start.ArgumentList.Add(localAddress.AbsoluteUri);
        start.ArgumentList.Add(userSid);
        return start;
    }

    public bool IsEnabled()
    {
        dynamic folder = Connect();
        dynamic? background = FindTask(folder, BootTask);
        dynamic? tray = FindTask(folder, TrayTask);
        return background is not null && tray is not null && (bool)background.Enabled && (bool)tray.Enabled;
    }

    public bool TrySetEnabled(bool enabled)
    {
        dynamic folder = Connect();
        dynamic? background = FindTask(folder, BootTask);
        dynamic? tray = FindTask(folder, TrayTask);
        if (enabled)
        {
            if (background is null || tray is null)
            {
                return false;
            }
            if (!MatchesAction(background, executable, BootArguments) || !MatchesAction(tray, TrayExecutable, TrayArguments))
            {
                return false;
            }
        }
        bool previousTray = tray is not null && (bool)tray.Enabled;
        try
        {
            if (tray is not null) tray.Enabled = enabled;
            if (background is not null) background.Enabled = enabled;
        }
        catch
        {
            if (tray is not null) tray.Enabled = previousTray;
            throw;
        }
        return true;
    }

    public void Register(string account, string password)
    {
        dynamic service = ConnectService();
        dynamic folder = service.GetFolder(@"\");
        dynamic logon = CreateDefinition(service, TaskTriggerLogon, TrayExecutable, TrayArguments);
        logon.Triggers.Item(1).UserId = userSid;
        dynamic boot = CreateDefinition(service, TaskTriggerBoot, executable, BootArguments);
        boot.Triggers.Item(1).Delay = "PT20S";

        string acl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{userSid})";
        dynamic background = folder.RegisterTaskDefinition(BootTask, boot, TaskCreateOrUpdate, account, password,
            TaskLogonPassword, acl);
        dynamic? tray = null;
        try
        {
            tray = folder.RegisterTaskDefinition(TrayTask, logon, TaskCreateOrUpdate, userSid, null,
                TaskLogonInteractiveToken, acl);
            tray.Enabled = true;
            background.Enabled = true;
        }
        catch
        {
            if (tray is not null) tray.Enabled = false;
            throw;
        }
    }

    private static dynamic CreateDefinition(dynamic service, int trigger, string path, string arguments)
    {
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "WebAccess Media 可攜版自動啟動";
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.MultipleInstances = 2;
        definition.Settings.Enabled = false;
        definition.Principal.RunLevel = 0;
        definition.Triggers.Create(trigger);
        dynamic action = definition.Actions.Create(TaskActionExec);
        action.Path = path;
        action.Arguments = arguments;
        action.WorkingDirectory = Path.GetDirectoryName(path);
        return definition;
    }

    private static bool MatchesAction(dynamic task, string path, string arguments)
    {
        dynamic action = task.Definition.Actions.Item(1);
        return string.Equals((string)action.Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string)action.Arguments, arguments, StringComparison.Ordinal);
    }

    private static string Quote(string value)
    {
        int trailingSlashes = value.Length - value.TrimEnd('\\').Length;
        return $"\"{value}{new string('\\', trailingSlashes)}\"";
    }

    private static dynamic Connect() => ConnectService().GetFolder(@"\");

    private static dynamic ConnectService()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static dynamic? FindTask(dynamic folder, string name)
    {
        try
        {
            return folder.GetTask(name);
        }
        catch (Exception error) when ((error is COMException or FileNotFoundException) && error.HResult == FileNotFound)
        {
            return null;
        }
    }
}
