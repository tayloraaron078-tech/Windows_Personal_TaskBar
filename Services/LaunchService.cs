using System;
using System.Diagnostics;
using System.IO;
using Personal_TaskBar.Models;

namespace Personal_TaskBar.Services;

/// <summary>
/// Handles launching entries and recording last-launch timestamps.
/// UseShellExecute = true is intentional: it lets Windows trigger UAC elevation
/// for executables that have a manifest requesting it, exactly like double-clicking.
/// </summary>
public class LaunchService
{
    private readonly ConfigService _configService;

    public LaunchService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Launches the given entry. Expands environment variables in the path,
    /// records the timestamp, and saves entries.toml.
    /// Returns true if launch succeeded.
    /// </summary>
    public bool Launch(Entry entry)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(entry.Path);

        try
        {
            var psi = new ProcessStartInfo
            {
                UseShellExecute = true, // required for UAC elevation and file associations
            };

            switch (entry.Type.ToLowerInvariant())
            {
                case "url":
                    // Shell opens the default browser for http/https/mailto URIs
                    psi.FileName = expandedPath;
                    break;

                case "folder":
                    // Open folder in Windows Explorer
                    psi.FileName  = "explorer.exe";
                    psi.Arguments = $"\"{expandedPath}\"";
                    break;

                case "file":
                    // Open with the registered handler for this file type
                    psi.FileName = expandedPath;
                    break;

                case "exe":
                default:
                    psi.FileName = expandedPath;
                    break;
            }

            Process.Start(psi);

            // Record the successful launch timestamp in ISO 8601
            entry.LastLaunched = DateTime.UtcNow.ToString("o");
            _configService.SaveEntries();

            return true;
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                $"Failed to launch \"{entry.Name}\":\n{ex.Message}",
                "Personal TaskBar – Launch Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>
    /// Returns true if the entry was launched within the last 7 days.
    /// Used to show the recency indicator dot.
    /// </summary>
    public static bool IsRecent(Entry entry)
    {
        if (string.IsNullOrEmpty(entry.LastLaunched))
            return false;

        if (!DateTime.TryParse(entry.LastLaunched, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
            return false;

        return (DateTime.UtcNow - ts).TotalDays <= 7;
    }

    /// <summary>Opens the folder that contains the entry's target in Explorer.</summary>
    public static void OpenFileLocation(Entry entry)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(entry.Path);

        try
        {
            if (File.Exists(expandedPath))
            {
                // /select highlights the file in the containing folder
                Process.Start("explorer.exe", $"/select,\"{expandedPath}\"");
            }
            else if (Directory.Exists(expandedPath))
            {
                Process.Start("explorer.exe", $"\"{expandedPath}\"");
            }
            else
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Path not found:\n{expandedPath}",
                    "Personal TaskBar",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                $"Could not open location:\n{ex.Message}",
                "Personal TaskBar",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }
}
