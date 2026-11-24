using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace WebAccess.Media.Tray;

internal static class StartupSetup
{
    private const uint DoNotPersist = 0x2;
    private const uint AlwaysShowUi = 0x80;
    private const uint GenericCredentials = 0x40000;
    private const uint KeepUsername = 0x100000;
    private const int Cancelled = 1223;

    public static void Configure(string[] args)
    {
        try
        {
            if (args.Length != 5 || !Path.IsPathFullyQualified(args[1]) || args[2].Contains('"') ||
                !Uri.TryCreate(args[3], UriKind.Absolute, out Uri? address) || !address.IsLoopback ||
                address.Scheme is not ("http" or "https"))
            {
                return;
            }
            SecurityIdentifier sid = new(args[4]);
            string account = sid.Translate(typeof(NTAccount)).Value;
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            string executable = Path.Combine(directory, "WebAccess.Media.exe");
            if (!File.Exists(executable))
            {
                Show("找不到同一套件的 WebAccess.Media.exe。請完整解壓縮 Windows ZIP。", MessageBoxIcon.Warning);
                return;
            }
            WindowsStartupTasks tasks = new(executable, args[1], args[2], address, sid.Value);
            StringBuilder user = new(account, 514);
            StringBuilder password = new(257);
            CredentialDialog dialog = new()
            {
                Size = Marshal.SizeOf<CredentialDialog>(),
                Caption = "WebAccess Media 開機自動啟動",
                Message = "請輸入目前 Windows 帳號的登入密碼。Windows 工作排程器會使用此帳號在登入前啟動監控。"
            };
            bool save = false;
            try
            {
                int result = Prompt(ref dialog, "WebAccess Media", IntPtr.Zero, 0, user, user.Capacity,
                    password, password.Capacity, ref save, DoNotPersist | AlwaysShowUi | GenericCredentials | KeepUsername);
                if (result == Cancelled) return;
                if (result != 0) throw new Win32Exception(result);
                tasks.Register(account, password.ToString());
                Show("已建立開機監控與登入通知區排程。請重新整理工程設定頁查看狀態。", MessageBoxIcon.Information);
            }
            finally
            {
                for (int index = 0; index < password.Length; index++) password[index] = '\0';
                password.Clear();
            }
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or Win32Exception or ArgumentException
            or IdentityNotMappedException)
        {
            Show($"無法建立開機排程（0x{error.HResult:X8}）。請確認 Windows 帳號密碼及批次登入權限。", MessageBoxIcon.Warning);
        }
    }

    private static void Show(string message, MessageBoxIcon icon) =>
        MessageBox.Show(message, "WebAccess Media", MessageBoxButtons.OK, icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredentialDialog
    {
        public int Size;
        public IntPtr Parent;
        public string Message;
        public string Caption;
        public IntPtr Banner;
    }

    [DllImport("credui.dll", EntryPoint = "CredUIPromptForCredentialsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int Prompt(ref CredentialDialog dialog, string target, IntPtr reserved, int authenticationError,
        StringBuilder user, int userLength, StringBuilder password, int passwordLength,
        [MarshalAs(UnmanagedType.Bool)] ref bool save, uint flags);
}
