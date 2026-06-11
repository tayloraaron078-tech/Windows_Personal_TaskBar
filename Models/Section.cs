namespace Personal_TaskBar.Models;

/// <summary>
/// Represents one named group of entries inside the bar.
/// Serialised as a TOML table in entries.toml.
/// </summary>
public class Section
{
    /// <summary>Human-readable label displayed on the section divider.</summary>
    public string Name        { get; set; } = "New Section";

    /// <summary>Hex colour for the accent line, e.g. "#4A90D9".</summary>
    public string AccentColor { get; set; } = "#4A90D9";

    /// <summary>icons_only | icons_labels | labels_only</summary>
    public string DisplayMode { get; set; } = "icons_labels";

    /// <summary>Whether the section is folded closed.</summary>
    public bool   Collapsed   { get; set; } = false;

    /// <summary>entries | scratchpad</summary>
    public string Type        { get; set; } = "entries";

    /// <summary>Text content for scratchpad sections.</summary>
    public string Content     { get; set; } = string.Empty;

    /// <summary>Ordered list of entries (empty for scratchpad sections).</summary>
    public List<Entry> Entries { get; set; } = new();
}
