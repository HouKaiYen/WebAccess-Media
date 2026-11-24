using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
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
    private const string BootTaskPrefix = "WebAccess Media - 開機監控 (";
    private const string TrayTaskPrefix = "WebAccess Media - 登入通知區 (";

    public string InstanceId { get; } = HashId(
        $"{executable.ToUpperInvariant()}|{userSid}|{localAddress.Port.ToString(CultureInfo.InvariantCulture)}");
    public string ShutdownEvent => $@"Global\WebAccess.Media.Startup.{InstanceId}";
    public string TrayExecutable => Path.Combine(Path.GetDirectoryName(executable)!, "tray", "WebAccess.Media.Tray.exe");
    private string ScheduleId { get; } = HashId($"{executable.ToUpperInvariant()}|{userSid}");
    private string BootTask => $"{BootTaskPrefix}{ScheduleId})";
    private string TrayTask => $"{TrayTaskPrefix}{ScheduleId})";
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

    public (bool IsEnabled, bool NeedsUpdate) ReadState()
    {
        dynamic folder = Connect();
        dynamic? background = FindTask(folder, BootTask);
        dynamic? tray = FindTask(folder, TrayTask);
        bool currentEnabled = background is not null && tray is not null && (bool)background.Enabled && (bool)tray.Enabled;
        bool legacyEnabled = FindLegacyTasks((object)folder).Any(IsTaskEnabled);
        bool needsUpdate = legacyEnabled || (currentEnabled && !MatchesCurrentActions(background!, tray!));
        return (currentEnabled || legacyEnabled, needsUpdate);
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
            if (!MatchesCurrentActions(background, tray))
            {
                return false;
            }
        }
        ApplyEnabledState(folder, background, tray, enabled);
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
        dynamic tray = folder.RegisterTaskDefinition(TrayTask, logon, TaskCreateOrUpdate, userSid, null,
            TaskLogonInteractiveToken, acl);
        ApplyEnabledState(folder, background, tray, enabled: true);
    }

    private void ApplyEnabledState(dynamic folder, object? background, object? tray, bool enabled)
    {
        List<(object Task, bool Enabled)> changes = FindLegacyTasks((object)folder).Select(task => (Task: task, Enabled: false)).ToList();
        if (tray is not null) changes.Add((tray, enabled));
        if (background is not null) changes.Add((background, enabled));

        Stack<(object Task, bool Enabled)> applied = new();
        try
        {
            foreach ((object task, bool desired) in changes)
            {
                bool previous = IsTaskEnabled(task);
                if (previous == desired) continue;
                ((dynamic)task).Enabled = desired;
                applied.Push((task, previous));
            }
        }
        catch
        {
            foreach ((object task, bool previous) in applied) ((dynamic)task).Enabled = previous;
            throw;
        }
    }

    private IEnumerable<object> FindLegacyTasks(dynamic folder)
    {
        foreach (dynamic task in folder.GetTasks(1))
        {
            string name = task.Name;
            string? path = name.StartsWith(BootTaskPrefix, StringComparison.Ordinal) && name != BootTask ? executable
                : name.StartsWith(TrayTaskPrefix, StringComparison.Ordinal) && name != TrayTask ? TrayExecutable : null;
            if (path is null || !name.EndsWith(')') || name.Length - name.LastIndexOf('(') != 18 ||
                !name.Substring(name.Length - 17, 16).All(Uri.IsHexDigit))
            {
                continue;
            }

            dynamic definition = task.Definition;
            if ((int)definition.Actions.Count != 1)
            {
                continue;
            }
            dynamic action = definition.Actions.Item(1);
            if ((int)action.Type == TaskActionExec && string.Equals((string)action.Path, path, StringComparison.OrdinalIgnoreCase) &&
                IsCurrentAccount((string)definition.Principal.UserId))
            {
                yield return task;
            }
        }
    }

    private bool IsCurrentAccount(string account)
    {
        if (string.Equals(account, userSid, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return new NTAccount(account).Translate(typeof(SecurityIdentifier)).Value == userSid;
        }
        catch (Exception error) when (error is IdentityNotMappedException or ArgumentException)
        {
            return false;
        }
    }

    private bool MatchesCurrentActions(dynamic background, dynamic tray) =>
        MatchesAction(background, executable, BootArguments) && MatchesAction(tray, TrayExecutable, TrayArguments);

    private static bool IsTaskEnabled(object task) => (bool)((dynamic)task).Enabled;
    private static string HashId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

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
