using System;
using System.Drawing;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Custom button control representing one entry inside a section.
/// Renders icon, label, and a recency dot depending on the section's display mode.
/// Left-click launches; right-click opens context menu.
/// </summary>
public class EntryButton : Panel
{
    // ── Data ────────────────────────────────────────────────────────────────

    public Entry      EntryModel    { get; }
    public string     DisplayMode   { get; set; } = "icons_labels";

    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly IconService   _iconService;
    private readonly LaunchService _launchService;
    private readonly ConfigService _configService;

    // ── Visual state ────────────────────────────────────────────────────────

    private bool  _hovered;
    private Image? _icon;
    private int   _iconSize = 48;

    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler?       LaunchRequested;
    public event EventHandler?       EditRequested;
    public event EventHandler<Section>? MoveToSectionRequested;
    public event EventHandler?       RemoveRequested;
    public event EventHandler?       OpenLocationRequested;

    public EntryButton(Entry entry, IconService iconService,
                       LaunchService launchService, ConfigService configService)
    {
        EntryModel     = entry;
        _iconService   = iconService;
        _launchService = launchService;
        _configService = configService;

        // Inherit Windows system colours so light/dark theme works automatically
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        Cursor    = Cursors.Hand;
        DoubleBuffered = true;

        BuildContextMenu();
        WireEvents();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Recalculates the control size and invalidates the display.
    /// Call whenever IconSize or DisplayMode changes.
    /// </summary>
    public void ApplyIconSize(int iconSize, string displayMode)
    {
        _iconSize   = iconSize;
        DisplayMode = displayMode;

        // Preload icon at new size (cache miss will trigger extraction)
        _icon = _iconService.GetIcon(EntryModel, iconSize);

        int padding  = Math.Max(4, iconSize / 8);
        int fontSize = Math.Max(7, iconSize / 5);
        Font = new Font(SystemFonts.DefaultFont.FontFamily, fontSize);

        switch (displayMode)
        {
            case "icons_only":
                Width  = iconSize + padding * 2;
                Height = iconSize + padding * 2;
                break;

            case "labels_only":
                Width  = 120;
                Height = fontSize * 2 + padding * 2;
                break;

            default: // icons_labels
                Width  = iconSize + padding * 2;
                Height = iconSize + fontSize * 2 + padding * 3;
                break;
        }

        Invalidate();
    }

    // ── Painting ────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_hovered ? SystemColors.ControlLight : SystemColors.Control);

        int padding  = Math.Max(4, _iconSize / 8);

        switch (DisplayMode)
        {
            case "icons_only":
                DrawIcon(g, padding, padding, _iconSize);
                DrawRecencyDot(g, Width - 8, 4);
                break;

            case "labels_only":
                DrawLabel(g, padding, padding, Width - padding * 2);
                DrawRecencyDot(g, Width - 8, Height / 2 - 4);
                break;

            default: // icons_labels
                DrawIcon(g, padding, padding, _iconSize);
                DrawLabel(g, padding, padding + _iconSize + padding, Width - padding * 2);
                DrawRecencyDot(g, Width - 8, 4);
                break;
        }
    }

    private void DrawIcon(Graphics g, int x, int y, int size)
    {
        _icon ??= _iconService.GetIcon(EntryModel, size);
        if (_icon != null)
            g.DrawImage(_icon, x, y, size, size);
    }

    private void DrawLabel(Graphics g, int x, int y, int maxWidth)
    {
        var fmt = new StringFormat
        {
            Trimming      = StringTrimming.EllipsisCharacter,
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
        };
        g.DrawString(EntryModel.Name, Font, SystemBrushes.ControlText,
                     new RectangleF(x, y, maxWidth, Font.Height * 2), fmt);
    }

    /// <summary>
    /// Draws a small green dot in the corner if this entry was launched recently.
    /// The dot signals "recently used" without cluttering the icon.
    /// </summary>
    private void DrawRecencyDot(Graphics g, int x, int y)
    {
        if (!LaunchService.IsRecent(EntryModel)) return;
        g.FillEllipse(Brushes.LimeGreen, x, y, 7, 7);
    }

    // ── Events wiring ───────────────────────────────────────────────────────

    private void WireEvents()
    {
        MouseEnter += (_, _) => { _hovered = true;  Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };

        Click += (_, _) =>
        {
            _launchService.Launch(EntryModel);
            Invalidate(); // refresh recency dot
            LaunchRequested?.Invoke(this, EventArgs.Empty);
        };

        // Tooltip for icons-only mode (and as a general accessibility aid)
        var tip = new ToolTip();
        tip.SetToolTip(this, EntryModel.Name);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMoveToMenu(menu);

        menu.Items.Add("Edit Entry",        null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Open File Location",null, (_, _) => OpenLocationRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove Entry",      null, OnRemoveClicked);

        ContextMenuStrip = menu;
    }

    /// <summary>
    /// Rebuilds the "Move to Section" submenu each time the context menu opens
    /// so it always reflects the current section list.
    /// </summary>
    private void RebuildMoveToMenu(ContextMenuStrip menu)
    {
        // Remove old "Move to Section" item if present
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i].Text == "Move to Section")
                menu.Items.RemoveAt(i);
        }

        var moveItem = new ToolStripMenuItem("Move to Section");
        foreach (var sec in _configService.Sections)
        {
            if (sec.Type == "scratchpad") continue;
            var capturedSec = sec;
            moveItem.DropDownItems.Add(sec.Name, null,
                (_, _) => MoveToSectionRequested?.Invoke(this, capturedSec));
        }

        // Insert before "Remove Entry"
        menu.Items.Insert(menu.Items.Count - 2, moveItem);
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            $"Remove \"{EntryModel.Name}\"?", "Personal TaskBar",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
            RemoveRequested?.Invoke(this, EventArgs.Empty);
    }
}
