namespace Personal_TaskBar.Models;

/// <summary>
/// One launchable item inside a section.
/// The Type field controls how LaunchService handles the path.
/// </summary>
public class Entry
{
    /// <summary>Display name shown under the icon (or in the labels-only list).</summary>
    public string Name          { get; set; } = "New Entry";

    /// <summary>exe | folder | file | url</summary>
    public string Type          { get; set; } = "exe";

    /// <summary>
    /// Target path or URL. May contain environment variable references
    /// like %USERPROFILE% which are expanded at launch time.
    /// </summary>
    public string Path          { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to a .ico or .png file that overrides the auto-extracted icon.
    /// Empty string means "use auto-extracted".
    /// </summary>
    public string IconOverride  { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp of the last successful launch, or empty if never launched.
    /// Used to show the recency indicator dot.
    /// </summary>
    public string LastLaunched  { get; set; } = string.Empty;
}
