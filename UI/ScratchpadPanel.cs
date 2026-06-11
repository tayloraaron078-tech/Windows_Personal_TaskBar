using System;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// A small multi-line text area that lives inside a scratchpad section.
/// Content is auto-saved to entries.toml on every keystroke with a 500ms debounce
/// so the user never loses notes without hammering disk on every character.
/// </summary>
public class ScratchpadPanel : Panel
{
    private readonly Section       _section;
    private readonly ConfigService _configService;
    private readonly RichTextBox   _textBox;
    private readonly System.Windows.Forms.Timer _debounceTimer;

    public ScratchpadPanel(Section section, ConfigService configService)
    {
        _section       = section;
        _configService = configService;

        _textBox = new RichTextBox
        {
            Dock           = DockStyle.Fill,
            Text           = section.Content,
            BackColor      = SystemColors.Window,
            ForeColor      = SystemColors.WindowText,
            BorderStyle    = BorderStyle.None,
            ScrollBars     = RichTextBoxScrollBars.Vertical,
            Multiline      = true,
            WordWrap       = true,
            Font           = SystemFonts.DefaultFont,
        };

        // Debounce timer fires 500ms after the last keystroke
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _section.Content = _textBox.Text;
            _configService.SaveEntries();
        };

        _textBox.TextChanged += (_, _) =>
        {
            // Restart the debounce window on every keystroke
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };

        Controls.Add(_textBox);
        BackColor = SystemColors.Window;
        MinimumSize = new System.Drawing.Size(100, 60);
        Height = 100;
    }

    /// <summary>Adjusts font size when the global icon-size slider changes.</summary>
    public void ApplyIconSize(int iconSize)
    {
        var fontSize = Math.Max(7, iconSize / 5);
        _textBox.Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, fontSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
