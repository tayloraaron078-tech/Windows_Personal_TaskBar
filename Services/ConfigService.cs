using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Personal_TaskBar.Models;
using Tomlyn;
using Tomlyn.Model;

namespace Personal_TaskBar.Services;

/// <summary>
/// Loads and saves config.toml and entries.toml from the same directory
/// as the running executable (portable mode).
/// On first run, creates both files with sensible defaults plus a
/// "Getting Started" sample section.
/// </summary>
public class ConfigService
{
    // ── Paths ───────────────────────────────────────────────────────────────

    private static readonly string BaseDir =
        System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    public static readonly string ConfigPath  = System.IO.Path.Combine(BaseDir, "config.toml");
    public static readonly string EntriesPath = System.IO.Path.Combine(BaseDir, "entries.toml");
    public static readonly string DocsDir     = System.IO.Path.Combine(BaseDir, "Docs");

    // ── Cached state ────────────────────────────────────────────────────────

    private AppConfig       _config  = new();
    private List<Section>   _sections = new();

    public AppConfig     Config   => _config;
    public List<Section> Sections => _sections;

    // ── Bootstrap ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates default config files if they do not exist, then loads both files.
    /// Safe to call multiple times.
    /// </summary>
    public void EnsureDefaults()
    {
        if (!File.Exists(ConfigPath))
            WriteDefaultConfig();

        if (!File.Exists(EntriesPath))
            WriteDefaultEntries();

        LoadConfig();
        LoadEntries();
    }

    // ── Load ────────────────────────────────────────────────────────────────

    /// <summary>Reads config.toml into the Config property.</summary>
    public void LoadConfig()
    {
        try
        {
            var text  = File.ReadAllText(ConfigPath);
            var table = Toml.ToModel(text);
            _config = ParseConfig(table);
        }
        catch
        {
            // Corrupt file – fall back to defaults and overwrite
            _config = new AppConfig();
            WriteDefaultConfig();
        }
    }

    /// <summary>Reads entries.toml into the Sections list.</summary>
    public void LoadEntries()
    {
        try
        {
            var text  = File.ReadAllText(EntriesPath);
            var table = Toml.ToModel(text);
            _sections = ParseSections(table);
        }
        catch
        {
            _sections = new List<Section>();
            WriteDefaultEntries();
        }
    }

    // ── Save ────────────────────────────────────────────────────────────────

    /// <summary>Persists the current Config object to config.toml.</summary>
    public void SaveConfig()
    {
        var sb = new System.Text.StringBuilder();
        var w  = _config.Window;
        var h  = _config.Hotkeys;
        var s  = _config.Startup;

        sb.AppendLine("[window]");
        sb.AppendLine($"x           = {w.X}");
        sb.AppendLine($"y           = {w.Y}");
        sb.AppendLine($"width       = {w.Width}");
        sb.AppendLine($"height      = {w.Height}");
        sb.AppendLine($"dock        = \"{w.Dock}\"");
        sb.AppendLine($"monitor     = {w.Monitor}");
        sb.AppendLine($"always_on_top = {w.AlwaysOnTop.ToString().ToLower()}");
        sb.AppendLine($"icon_size   = {w.IconSize}");
        sb.AppendLine();
        sb.AppendLine("[hotkeys]");
        sb.AppendLine($"toggle_visibility = \"{h.ToggleVisibility}\"");
        sb.AppendLine($"search            = \"{h.Search}\"");
        sb.AppendLine();
        sb.AppendLine("[startup]");
        sb.AppendLine($"launch_with_windows = {s.LaunchWithWindows.ToString().ToLower()}");

        File.WriteAllText(ConfigPath, sb.ToString());
    }

    /// <summary>Persists the current Sections list to entries.toml.</summary>
    public void SaveEntries()
    {
        var sb = new System.Text.StringBuilder();

        foreach (var section in _sections)
        {
            sb.AppendLine("[[sections]]");
            sb.AppendLine($"name         = \"{EscapeToml(section.Name)}\"");
            sb.AppendLine($"accent_color = \"{EscapeToml(section.AccentColor)}\"");
            sb.AppendLine($"display_mode = \"{EscapeToml(section.DisplayMode)}\"");
            sb.AppendLine($"collapsed    = {section.Collapsed.ToString().ToLower()}");
            sb.AppendLine($"type         = \"{EscapeToml(section.Type)}\"");

            if (section.Type == "scratchpad")
            {
                sb.AppendLine($"content = \"\"\"{EscapeTomlMultiline(section.Content)}\"\"\"");
            }
            else
            {
                foreach (var entry in section.Entries)
                {
                    sb.AppendLine($"  [[sections.entries]]");
                    sb.AppendLine($"  name          = \"{EscapeToml(entry.Name)}\"");
                    sb.AppendLine($"  type          = \"{EscapeToml(entry.Type)}\"");
                    sb.AppendLine($"  path          = \"{EscapeToml(entry.Path)}\"");
                    sb.AppendLine($"  icon_override = \"{EscapeToml(entry.IconOverride)}\"");
                    sb.AppendLine($"  last_launched = \"{EscapeToml(entry.LastLaunched)}\"");
                }
            }

            sb.AppendLine();
        }

        File.WriteAllText(EntriesPath, sb.ToString());
    }

    // ── Parsing helpers ─────────────────────────────────────────────────────

    private static AppConfig ParseConfig(TomlTable t)
    {
        var cfg = new AppConfig();

        if (t.TryGetValue("window", out var wObj) && wObj is TomlTable wt)
        {
            cfg.Window.X           = Get(wt, "x",             cfg.Window.X);
            cfg.Window.Y           = Get(wt, "y",             cfg.Window.Y);
            cfg.Window.Width       = Get(wt, "width",         cfg.Window.Width);
            cfg.Window.Height      = Get(wt, "height",        cfg.Window.Height);
            cfg.Window.Dock        = GetStr(wt, "dock",       cfg.Window.Dock);
            cfg.Window.Monitor     = Get(wt, "monitor",       cfg.Window.Monitor);
            cfg.Window.AlwaysOnTop = GetBool(wt, "always_on_top", cfg.Window.AlwaysOnTop);
            cfg.Window.IconSize    = Math.Clamp(Get(wt, "icon_size", cfg.Window.IconSize), 24, 96);
        }

        if (t.TryGetValue("hotkeys", out var hObj) && hObj is TomlTable ht)
        {
            cfg.Hotkeys.ToggleVisibility = GetStr(ht, "toggle_visibility", cfg.Hotkeys.ToggleVisibility);
            cfg.Hotkeys.Search           = GetStr(ht, "search",            cfg.Hotkeys.Search);
        }

        if (t.TryGetValue("startup", out var sObj) && sObj is TomlTable st)
        {
            cfg.Startup.LaunchWithWindows = GetBool(st, "launch_with_windows", cfg.Startup.LaunchWithWindows);
        }

        return cfg;
    }

    private static List<Section> ParseSections(TomlTable t)
    {
        var list = new List<Section>();

        if (!t.TryGetValue("sections", out var sArr) || sArr is not TomlTableArray arr)
            return list;

        foreach (TomlTable st in arr)
        {
            var sec = new Section
            {
                Name        = GetStr(st, "name",         "Section"),
                AccentColor = GetStr(st, "accent_color", "#4A90D9"),
                DisplayMode = GetStr(st, "display_mode", "icons_labels"),
                Collapsed   = GetBool(st, "collapsed",   false),
                Type        = GetStr(st, "type",         "entries"),
                Content     = GetStr(st, "content",      string.Empty),
            };

            if (st.TryGetValue("entries", out var eArr) && eArr is TomlTableArray entries)
            {
                foreach (TomlTable et in entries)
                {
                    sec.Entries.Add(new Entry
                    {
                        Name         = GetStr(et, "name",          "Entry"),
                        Type         = GetStr(et, "type",          "exe"),
                        Path         = GetStr(et, "path",          string.Empty),
                        IconOverride = GetStr(et, "icon_override", string.Empty),
                        LastLaunched = GetStr(et, "last_launched", string.Empty),
                    });
                }
            }

            list.Add(sec);
        }

        return list;
    }

    // ── Default file writers ────────────────────────────────────────────────

    private void WriteDefaultConfig()
    {
        var cfg = new AppConfig();
        _config = cfg;
        SaveConfig();
    }

    private void WriteDefaultEntries()
    {
        var quickstartPath = System.IO.Path.Combine(DocsDir, "QUICKSTART.md");

        _sections = new List<Section>
        {
            new Section
            {
                Name        = "Getting Started",
                AccentColor = "#4A90D9",
                DisplayMode = "icons_labels",
                Type        = "entries",
                Entries     = new List<Entry>
                {
                    new Entry
                    {
                        Name = "Quick Start Guide",
                        Type = "file",
                        Path = quickstartPath,
                    }
                }
            }
        };

        SaveEntries();
    }

    // ── TOML value helpers ──────────────────────────────────────────────────

    private static int GetInt(object v, int fallback) =>
        v is long l ? (int)l : v is int i ? i : fallback;

    private static int Get(TomlTable t, string key, int fallback) =>
        t.TryGetValue(key, out var v) ? GetInt(v, fallback) : fallback;

    private static string GetStr(TomlTable t, string key, string fallback) =>
        t.TryGetValue(key, out var v) && v is string s ? s : fallback;

    private static bool GetBool(TomlTable t, string key, bool fallback) =>
        t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    // Escape backslashes and double-quotes for single-line TOML strings
    private static string EscapeToml(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // For multi-line TOML strings, only """ needs escaping (very rare in scratchpad text)
    private static string EscapeTomlMultiline(string s) =>
        s.Replace("\"\"\"", "\\\"\\\"\\\"");
}
