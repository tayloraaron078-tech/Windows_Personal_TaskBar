using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Personal_TaskBar.Models;

namespace Personal_TaskBar.Services;

/// <summary>
/// Extracts icons for entries and caches them in memory so repeated renders
/// never re-hit the filesystem or shell.
/// Cache key = (path, iconSize).
/// </summary>
public class IconService : IDisposable
{
    // ── In-memory cache ─────────────────────────────────────────────────────

    private readonly Dictionary<string, Image> _cache = new(StringComparer.OrdinalIgnoreCase);

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a cached icon image for the entry at the requested size.
    /// Falls back through: custom override → exe extraction → shell icon → generic.
    /// </summary>
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

    /// <summary>Pre-warms the cache for all entries in the provided sections.</summary>
    public void Preload(IEnumerable<Section> sections, int size)
    {
        foreach (var sec in sections)
            foreach (var entry in sec.Entries)
                GetIcon(entry, size);
    }

    // ── Icon loading logic ──────────────────────────────────────────────────

    private Image LoadIcon(Entry entry, string expandedPath, int size)
    {
        // 1. Custom icon override (.ico or .png)
        if (!string.IsNullOrEmpty(entry.IconOverride))
        {
            var overridePath = Environment.ExpandEnvironmentVariables(entry.IconOverride).Trim('"');
            try
            {
                var ext = Path.GetExtension(overridePath).ToLower();
                if (ext == ".ico")
                {
                    using var ico = new Icon(overridePath, size, size);
                    return new Bitmap(ico.ToBitmap(), size, size);
                }
                else if (ext == ".png" || ext == ".bmp" || ext == ".jpg" || ext == ".jpeg")
                {
                    using var raw = Image.FromFile(overridePath);
                    return new Bitmap(raw, size, size);
                }
            }
            catch { /* fall through */ }
        }

        // 2. For .exe, use ExtractIcon from shell32
        if (entry.Type == "exe" && File.Exists(expandedPath))
        {
            var img = ExtractExeIcon(expandedPath, size);
            if (img != null) return img;
        }

        // 3. Shell icon via SHGetFileInfo (handles folders, file associations, URLs)
        {
            var img = GetShellIcon(expandedPath, entry.Type, size);
            if (img != null) return img;
        }

        // 4. Absolute fallback: system generic file icon
        return SystemIcons.WinLogo.ToBitmap();
    }

    private static Image? ExtractExeIcon(string exePath, int size)
    {
        var hIcon = NativeMethods.ExtractIcon(IntPtr.Zero, exePath, 0);
        if (hIcon == IntPtr.Zero || hIcon == new IntPtr(1))
            return null;

        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return new Bitmap(icon.ToBitmap(), size, size);
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static Image? GetShellIcon(string path, string entryType, int size)
    {
        // Get the system image list index via SHGetFileInfo (no icon extracted yet)
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
            return GetShellIconLegacy(queryPath, entryType, pathExists, size);

        // For sizes > 32, use SHGetImageList which has 48x48 (EXTRALARGE) and 256x256 (JUMBO)
        int shil = size >= 64 ? NativeMethods.SHIL_JUMBO
                 : size >= 36 ? NativeMethods.SHIL_EXTRALARGE
                 : size >= 24 ? NativeMethods.SHIL_LARGE
                 :               NativeMethods.SHIL_SMALL;

        try
        {
            var iid = NativeMethods.IID_IImageList;
            NativeMethods.SHGetImageList(shil, ref iid, out var imageList);
            imageList.GetIcon(shfi.iIcon, 0x0001 /* ILD_TRANSPARENT */, out var hIcon);
            if (hIcon == IntPtr.Zero) goto legacy;

            try
            {
                using var icon = Icon.FromHandle(hIcon);
                return new Bitmap(icon.ToBitmap(), size, size);
            }
            finally { NativeMethods.DestroyIcon(hIcon); }
        }
        catch { /* fall through to legacy */ }

        legacy:
        return GetShellIconLegacy(queryPath, entryType, pathExists, size);
    }

    private static Image? GetShellIconLegacy(string path, string entryType, bool pathExists, int size)
    {
        var shfi  = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
        if (!pathExists) flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var result = NativeMethods.SHGetFileInfo(path, NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(shfi.hIcon);
            return new Bitmap(icon.ToBitmap(), size, size);
        }
        finally { NativeMethods.DestroyIcon(shfi.hIcon); }
    }

    // ── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var img in _cache.Values)
            img.Dispose();
        _cache.Clear();
    }
}
