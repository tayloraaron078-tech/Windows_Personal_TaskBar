using System;
using System.Runtime.InteropServices;

namespace Personal_TaskBar;

/// <summary>
/// P/Invoke declarations for Win32 APIs used throughout the application.
/// Centralised here to keep all unsafe interop in one auditable place.
/// </summary>
internal static class NativeMethods
{
    // ── Drag helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Releases the mouse capture from a control so that SendMessage with
    /// WM_NCLBUTTONDOWN can hand the drag loop over to the OS.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    // WM_NCLBUTTONDOWN – tells DefWindowProc to begin a window move/size loop
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION        = 2;

    // ── Hotkey registration ─────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    // ── Window management ───────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST   = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── DWM (Windows 11 visual chrome) ─────────────────────────────────────

    [DllImport("dwmapi.dll", PreserveSig = false)]
    public static extern void DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute,
        ref int pvAttribute, int cbAttribute);

    // DWMWA_WINDOW_CORNER_PREFERENCE – rounded corners on Windows 11
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DEFAULT    = 0;
    public const int DWMWCP_ROUND      = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // DWMWA_BORDER_COLOR – border accent colour (0xFFBBGGRR or DWMWA_COLOR_NONE)
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_COLOR_NONE   = unchecked((int)0xFFFFFFFE);

    // ── Icon extraction ─────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── Single-instance activation broadcast ───────────────────────────────

    private const string ActivateMessageName = "Personal_TaskBar_Activate";

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static readonly uint WM_ACTIVATE_INSTANCE = RegisterWindowMessage(ActivateMessageName);

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

    public const uint SHGFI_ICON              = 0x000000100;
    public const uint SHGFI_SYSICONINDEX      = 0x000004000;
    public const uint SHGFI_SMALLICON         = 0x000000001;
    public const uint SHGFI_LARGEICON         = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint FILE_ATTRIBUTE_NORMAL   = 0x00000080;

    // ── SHGetImageList (for 48x48 and 256x256 icons) ───────────────────────

    public const int SHIL_SMALL      = 1;  // 16x16
    public const int SHIL_LARGE      = 0;  // 32x32
    public const int SHIL_EXTRALARGE = 2;  // 48x48
    public const int SHIL_JUMBO      = 4;  // 256x256

    [DllImport("shell32.dll", PreserveSig = false)]
    public static extern void SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    public static readonly Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, uint crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, uint flags, out IntPtr picon);
        // remaining methods not needed
    }
}
