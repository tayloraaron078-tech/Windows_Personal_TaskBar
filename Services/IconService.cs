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
/// Icon rendering notes:
/// - Icon.ToBitmap() / Graphics.DrawIcon() both fail to write alpha values when
///   rendering a GDI icon onto a 32-bpp DIB section — the result looks invisible.
/// - DrawIconEx with DI_NORMAL is the correct Win32 call; it populates alpha correctly.
/// - Icon.ExtractAssociatedIcon is used for .exe files; it is a managed .NET wrapper
///   that calls SHGetFileInfo internally and is safe on all supported Windows versions.
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

        Image img;
        try   { img = LoadIcon(entry, expandedPath, size); }
        catch { img = FallbackIcon(size); }

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
        // 1. Custom icon override (.ico / image file)
        if (!string.IsNullOrEmpty(entry.IconOverride))
        {
            var overridePath = Environment.ExpandEnvironmentVariables(entry.IconOverride).Trim('"');
            var img = TryLoadOverride(overridePath, size);
            if (img != null) return img;
        }

        // 2. For .exe: Icon.ExtractAssociatedIcon is a safe managed wrapper
        if (entry.Type.Equals("exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(expandedPath))
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(expandedPath);
                if (ico != null) return HIconToBitmap(ico.Handle, size);
            }
            catch { /* fall through */ }
        }

        // 3. Shell icon via SHGetFileInfo (handles folders, other file types, URLs)
        {
            var img = GetShellIcon(expandedPath, entry.Type, size);
            if (img != null) return img;
        }

        // 4. Absolute fallback
        return FallbackIcon(size);
    }

    // ── Override loader ───────────────────────────────────────────────────────

    private static Image? TryLoadOverride(string path, int size)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ico")
            {
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

    // ── Shell icon ────────────────────────────────────────────────────────────

    private static Image? GetShellIcon(string path, string entryType, int size)
    {
        try
        {
            var shfi  = new NativeMethods.SHFILEINFO();
            uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;

            bool pathExists = File.Exists(path) || Directory.Exists(path);
            if (!pathExists)
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                if (entryType.Equals("url", StringComparison.OrdinalIgnoreCase))
                    path = ".html";
            }

            var result = NativeMethods.SHGetFileInfo(path, NativeMethods.FILE_ATTRIBUTE_NORMAL,
                ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), flags);

            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                return null;

            try   { return HIconToBitmap(shfi.hIcon, size); }
            finally { NativeMethods.DestroyIcon(shfi.hIcon); }
        }
        catch { return null; }
    }

    // ── HICON → Bitmap ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an HICON to a correctly-rendered ARGB Bitmap using DrawIconEx.
    /// This is the only reliable way to get proper transparency from an HICON —
    /// Icon.ToBitmap() and Graphics.DrawIcon() both produce bitmaps where the
    /// alpha channel is 0 (invisible) on 32bpp DIB sections.
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

    private static Bitmap FallbackIcon(int size)
    {
        // Draw a simple placeholder glyph rather than a Windows logo
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var r = new Rectangle(2, 2, size - 4, size - 4);
        using var br = new SolidBrush(Color.FromArgb(180, 100, 100, 100));
        g.FillRectangle(br, r);
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
