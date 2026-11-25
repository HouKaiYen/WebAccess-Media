using System.Runtime.InteropServices;

namespace WebAccess.Media;

internal static class WindowsLaunch
{
    private const uint ParentProcess = uint.MaxValue;
    private const uint WarningIcon = 0x30;
    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint DiskFile = 1;
    private const uint Pipe = 3;

    internal static void AttachParentConsole()
    {
        nint output = GetStdHandle(StandardOutput);
        nint error = GetStdHandle(StandardError);
        if (!AttachConsole(ParentProcess)) return;

        // 附加主控台可能變更標準輸出控制代碼，保留呼叫端既有的重新導向。
        if (GetFileType(output) is DiskFile or Pipe) SetStdHandle(StandardOutput, output);
        if (GetFileType(error) is DiskFile or Pipe) SetStdHandle(StandardError, error);
    }

    internal static void ShowWarning(string message) =>
        MessageBoxW(0, message, "WebAccess Media", WarningIcon);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int handle, nint value);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint window, string message, string caption, uint options);
}
