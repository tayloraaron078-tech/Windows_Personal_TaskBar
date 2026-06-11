using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Search bar that overlays the top of the bar.
/// Activated by the global search hotkey or the search icon in the control strip.
/// Filters all entries live as the user types.
/// Enter launches the top result. Escape restores normal view.
/// </summary>
public class SearchOverlay : Panel
{
    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly ConfigService  _configService;
    private readonly LaunchService  _launchService;

    // ── Controls ────────────────────────────────────────────────────────────

    private readonly TextBox _input;

    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the search overlay should be hidden and the normal view restored.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised on every keystroke with the current filter text.
    /// The MainForm responds by showing/hiding section panels.
    /// </summary>
    public event EventHandler<string>? FilterChanged;

    // ── Constructor ─────────────────────────────────────────────────────────

    public SearchOverlay(ConfigService configService, LaunchService launchService)
    {
        _configService = configService;
        _launchService = launchService;

        BackColor = SystemColors.Control;
        Dock      = DockStyle.Top;
        Height    = 32;

        _input = new TextBox
        {
            Dock        = DockStyle.Fill,
            PlaceholderText = "Search entries… (Enter to launch, Esc to close)",
            BackColor   = SystemColors.Window,
            ForeColor   = SystemColors.WindowText,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = SystemFonts.DefaultFont,
        };

        _input.TextChanged += (_, _) => FilterChanged?.Invoke(this, _input.Text);

        _input.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;

                case Keys.Enter:
                    LaunchTopResult(_input.Text);
                    e.Handled = true;
                    break;
            }
        };

        Controls.Add(_input);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Shows the overlay and focuses the input.</summary>
    public void Activate()
    {
        _input.Clear();
        Visible = true;
        _input.Focus();
    }

    /// <summary>Hides the overlay and clears the filter.</summary>
    public void Deactivate()
    {
        Visible = false;
        _input.Clear();
        FilterChanged?.Invoke(this, string.Empty); // restore all sections
    }

    // ── Launch top result ───────────────────────────────────────────────────

    private void LaunchTopResult(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Find the first matching entry across all sections
        var match = _configService.Sections
            .Where(s => s.Type == "entries")
            .SelectMany(s => s.Entries)
            .FirstOrDefault(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        if (match != null)
            _launchService.Launch(match);

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
