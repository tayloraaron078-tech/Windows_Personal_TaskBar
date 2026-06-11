namespace Personal_TaskBar.Models;

/// <summary>
/// Root model for config.toml.
/// Every public property maps 1-to-1 with a TOML key so ConfigService can
/// serialise/deserialise without hand-written mapping code.
/// </summary>
public class AppConfig
{
    public WindowConfig   Window   { get; set; } = new();
    public HotkeyConfig   Hotkeys  { get; set; } = new();
    public StartupConfig  Startup  { get; set; } = new();
}

/// <summary>Persisted window geometry and behaviour flags.</summary>
public class WindowConfig
{
    public int    X          { get; set; } = 100;
    public int    Y          { get; set; } = 100;
    public int    Width      { get; set; } = 400;
    public int    Height     { get; set; } = 80;
    /// <summary>One of: none, top, bottom, left, right</summary>
    public string Dock       { get; set; } = "none";
    /// <summary>Zero-based index of the monitor the bar lives on.</summary>
    public int    Monitor    { get; set; } = 0;
    public bool   AlwaysOnTop { get; set; } = true;
    /// <summary>Drives all sizing. Range 24–96 px.</summary>
    public int    IconSize   { get; set; } = 48;
}

/// <summary>Global hotkey strings (e.g. "Ctrl+`", "Ctrl+Space").</summary>
public class HotkeyConfig
{
    public string ToggleVisibility { get; set; } = "Ctrl+`";
    public string Search           { get; set; } = "Ctrl+Space";
}

/// <summary>Windows startup integration.</summary>
public class StartupConfig
{
    public bool LaunchWithWindows { get; set; } = false;
}
