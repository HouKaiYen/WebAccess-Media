using System.Dynamic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class WindowsStartupSteps
{
    private object _originalBoot = null!;
    private object _originalTray = null!;
    private object _boot = null!;
    private object _tray = null!;

    [Given("Windows 已有可用的開機與登入排程")]
    public void GivenExistingTasks()
    {
        _boot = _originalBoot = CreateDefinition();
        _tray = _originalTray = CreateDefinition();
    }

    [When("更新排程時「(.*)」註冊失敗")]
    public void WhenTaskRegistrationFails(string failingTask)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("這個情境驗證 Windows 排程更新。");
            return;
        }
        WindowsStartupTasks tasks = new(@"C:\Test\WebAccess.Media.exe", @"C:\Test\data", "http://localhost:3008",
            new Uri("http://localhost:3008"), "S-1-5-21-1-2-3-1000");
        dynamic scheduler = new ExpandoObject();
        dynamic folder = new ExpandoObject();
        dynamic registeredTray = new ExpandoObject();
        registeredTray.Definition = _originalTray;
        registeredTray.Enabled = true;
        registeredTray.GetSecurityDescriptor = (Func<int, string>)(_ => "D:P");
        scheduler.GetFolder = (Func<string, object>)(_ => (object)folder);
        scheduler.NewTask = (Func<int, object>)(_ => CreateDefinition());
        folder.GetTask = (Func<string, object>)(_ => (object)registeredTray);
        folder.GetTasks = (Func<int, object[]>)(_ => Array.Empty<object>());
        Action<string, object, int, string, string?, int, string?> register = (name, definition, _, _, _, _, _) =>
        {
            if (name.Contains(failingTask, StringComparison.Ordinal))
                throw new COMException("測試排程註冊失敗。");
            if (name.Contains("開機監控", StringComparison.Ordinal)) _boot = definition;
            else _tray = definition;
        };
        folder.RegisterTaskDefinition = register;

        Assert.Throws<COMException>(() =>
        {
            if (OperatingSystem.IsWindows()) tasks.Register((object)scheduler, "test", "");
        });
    }

    [Then("原有的開機與登入排程保持啟用")]
    public void ThenOriginalTasksAreEnabled()
    {
        Assert.That(_boot, Is.SameAs(_originalBoot));
        Assert.That(_tray, Is.SameAs(_originalTray));
        Assert.That((bool)((dynamic)_boot).Settings.Enabled, Is.True);
        Assert.That((bool)((dynamic)_tray).Settings.Enabled, Is.True);
    }

    private static object CreateDefinition()
    {
        dynamic definition = new ExpandoObject();
        dynamic trigger = new ExpandoObject();
        dynamic action = new ExpandoObject();
        definition.RegistrationInfo = new ExpandoObject();
        definition.Settings = new ExpandoObject();
        definition.Settings.Enabled = true;
        definition.Principal = new ExpandoObject();
        definition.Triggers = new ExpandoObject();
        definition.Triggers.Create = (Func<int, object>)(_ => (object)trigger);
        definition.Triggers.Item = (Func<int, object>)(_ => (object)trigger);
        definition.Actions = new ExpandoObject();
        definition.Actions.Create = (Func<int, object>)(_ => (object)action);
        return definition;
    }
}
