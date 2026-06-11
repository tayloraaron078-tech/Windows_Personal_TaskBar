using System;
using System.Runtime.InteropServices;

namespace Personal_TaskBar;

/// <summary>
/// P/Invoke declarations for Win32 APIs used throughout the application.
/// Centralised here to keep all unsafe interop in one auditable place.
/// </summary>
internal static class NativeMethods
{
    // ── Hotkey registration ─────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags for RegisterHotKey
    public const uint MOD_ALT     = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const uint MOD_WIN     = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // WM_HOTKEY message identifier
    public const int WM_HOTKEY = 0x0312;

    // ── Window management ───────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST    = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST  = new(-2);
    public const uint SWP_NOMOVE  = 0x0002;
    public const uint SWP_NOSIZE  = 0x0001;

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── Icon extraction ─────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── Single-instance activation broadcast ───────────────────────────────

    // Custom window message that the second instance sends to the first
    private const string ActivateMessageName = "Personal_TaskBar_Activate";

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static readonly uint WM_ACTIVATE_INSTANCE = RegisterWindowMessage(ActivateMessageName);

    /// <summary>
    /// Called by the second instance to signal the first to come to the foreground.
    /// Uses HWND_BROADCAST so the first instance receives it regardless of its handle.
    /// </summary>
    public static void BroadcastActivateMessage()
    {
        PostMessage(new IntPtr(0xFFFF), WM_ACTIVATE_INSTANCE, IntPtr.Zero, IntPtr.Zero);
    }

    // ── Shell icon helper ───────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    public const uint SHGFI_ICON       = 0x000000100;
    public const uint SHGFI_SMALLICON  = 0x000000001;
    public const uint SHGFI_LARGEICON  = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint FILE_ATTRIBUTE_NORMAL   = 0x00000080;
}
