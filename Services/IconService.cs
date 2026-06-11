using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Personal_TaskBar.Models;

namespace Personal_TaskBar.Services;

/// <summary>
/// Extracts icons for entries and caches them in memory.
/// Cache key = (iconOverride | expandedPath | size).
///
/// Root cause of the "no icon" bug that was here previously:
///   Icon.ToBitmap() does NOT correctly handle the AND mask used by 32x32 legacy
///   icons, producing a fully-transparent (invisible) bitmap.  All HICON → Bitmap
///   conversions now go through HIconToBitmap() which uses DrawIconEx.
/// </summary>
public class IconService : IDisposable
{
    private readonly Dictionary<string, Image> _cache = new(StringComparer.OrdinalIgnoreCase);

    // ── Public API ────────────────────────────────────────────────────────────

    public Image GetIcon(Entry entry, int size)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(entry.Path).Trim('"');
        var cacheKey     = $"{entry.IconOverride}|{expandedPath}|{size}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var img = LoadIcon(entry, expandedPath, size);
        _cache[cacheKey] = img;
        return img;
    }

    public void Preload(IEnumerable<Section> sections, int size)
    {
        foreach (var sec in sections)
            foreach (var entry in sec.Entries)
                GetIcon(entry, size);
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private Image LoadIcon(Entry entry, string expandedPath, int size)
    {
        // 1. Custom icon override (.ico / .png / .bmp / .jpg)
        if (!string.IsNullOrEmpty(entry.IconOverride))
        {
            var overridePath = Environment.ExpandEnvironmentVariables(entry.IconOverride).Trim('"');
            var img = TryLoadOverride(overridePath, size);
            if (img != null) return img;
        }

        // 2. Shell icon via SHGetImageList (proper 48x48 / 256x256 quality)
        {
            var img = GetShellIcon(expandedPath, entry.Type, size);
            if (img != null) return img;
        }

        // 3. Absolute fallback
        return HIconToBitmap(SystemIcons.WinLogo.Handle, size);
    }

    // ── Override loader ───────────────────────────────────────────────────────

    private static Image? TryLoadOverride(string path, int size)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ico")
            {
                // Load .ico through HICON so DrawIconEx handles the mask correctly
                using var ico = new Icon(path, size, size);
                return HIconToBitmap(ico.Handle, size);
            }
            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg")
            {
                using var raw = Image.FromFile(path);
                return new Bitmap(raw, size, size);
            }
        }
        catch { /* fall through */ }
        return null;
    }

    // ── Shell icon via SHGetImageList ─────────────────────────────────────────

    private static Image? GetShellIcon(string path, string entryType, int size)
    {
        // First, get the system image list index via SHGetFileInfo
        var shfi  = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_SYSICONINDEX | NativeMethods.SHGFI_LARGEICON;

        bool pathExists = File.Exists(path) || Directory.Exists(path);
        string queryPath = path;
        if (!pathExists)
        {
            flags    |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            queryPath = entryType == "url" ? ".html" : path;
        }

        var result = NativeMethods.SHGetFileInfo(queryPath, NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), flags);

        if (result == IntPtr.Zero)
            return null;

        // Pick the image list that matches the requested size
        int shil = size >= 64 ? NativeMethods.SHIL_JUMBO
                 : size >= 36 ? NativeMethods.SHIL_EXTRALARGE
                 : size >= 24 ? NativeMethods.SHIL_LARGE
                 :               NativeMethods.SHIL_SMALL;

        try
        {
            var iid = NativeMethods.IID_IImageList;
            NativeMethods.SHGetImageList(shil, ref iid, out var imageList);

            imageList.GetIcon(shfi.iIcon, 0x0001 /* ILD_TRANSPARENT */, out var hIcon);
            if (hIcon == IntPtr.Zero)
                return FallbackShellIcon(queryPath, flags, pathExists, size);

            try   { return HIconToBitmap(hIcon, size); }
            finally { NativeMethods.DestroyIcon(hIcon); }
        }
        catch { /* SHGetImageList unavailable – fall through */ }

        return FallbackShellIcon(queryPath, flags, pathExists, size);
    }

    /// <summary>
    /// Legacy fallback: SHGetFileInfo with SHGFI_ICON (32x32 max, scaled up).
    /// Used when SHGetImageList is unavailable.
    /// </summary>
    private static Image? FallbackShellIcon(string path, uint baseFlags, bool pathExists, int size)
    {
        var shfi  = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
        if (!pathExists) flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var result = NativeMethods.SHGetFileInfo(path, NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

        try   { return HIconToBitmap(shfi.hIcon, size); }
        finally { NativeMethods.DestroyIcon(shfi.hIcon); }
    }

    // ── HICON → Bitmap using DrawIconEx ──────────────────────────────────────

    /// <summary>
    /// Converts an HICON to a Bitmap using DrawIconEx.
    /// Icon.ToBitmap() / Graphics.DrawIcon() do NOT correctly handle the AND mask
    /// on legacy 32x32 icons — the result has alpha=0 everywhere (invisible).
    /// DrawIconEx uses the Windows icon renderer and produces a correct ARGB bitmap.
    /// </summary>
    private static Bitmap HIconToBitmap(IntPtr hIcon, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var hdc = g.GetHdc();
        try
        {
            NativeMethods.DrawIconEx(hdc, 0, 0, hIcon, size, size, 0, IntPtr.Zero,
                NativeMethods.DI_NORMAL);
        }
        finally { g.ReleaseHdc(hdc); }
        return bmp;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var img in _cache.Values)
            img.Dispose();
        _cache.Clear();
    }
}
