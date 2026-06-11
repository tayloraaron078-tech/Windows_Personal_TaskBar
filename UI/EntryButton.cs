using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Custom panel representing one entry (app / file / folder) in the bar.
/// Renders an icon and/or label with a Windows 11-style rounded-rectangle hover highlight.
/// Left-click launches; right-click shows context menu.
/// </summary>
public class EntryButton : Panel
{
    // ── Data ─────────────────────────────────────────────────────────────────

    public Entry  EntryModel  { get; }
    public string DisplayMode { get; private set; } = "icons_labels";

    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly IconService   _iconService;
    private readonly LaunchService _launchService;
    private readonly ConfigService _configService;

    // ── Visual state ─────────────────────────────────────────────────────────

    private bool   _hovered;
    private bool   _pressed;
    private Image? _icon;
    private int    _iconSize  = 48;
    private Font   _labelFont = new Font("Segoe UI", 8f);

    // Corner radius for the hover pill
    private const int Radius = 6;

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler?          LaunchRequested;
    public event EventHandler?          EditRequested;
    public event EventHandler<Section>? MoveToSectionRequested;
    public event EventHandler?          RemoveRequested;
    public event EventHandler?          OpenLocationRequested;

    // ── Constructor ──────────────────────────────────────────────────────────

    public EntryButton(Entry entry, IconService iconService,
                       LaunchService launchService, ConfigService configService)
    {
        EntryModel     = entry;
        _iconService   = iconService;
        _launchService = launchService;
        _configService = configService;

        BackColor      = Color.Transparent;
        ForeColor      = SystemColors.WindowText;
        Cursor         = Cursors.Hand;
        DoubleBuffered = true;

        BuildContextMenu();
        WireEvents();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ApplyIconSize(int iconSize, string displayMode)
    {
        _iconSize   = iconSize;
        DisplayMode = displayMode;

        _icon = _iconService.GetIcon(EntryModel, iconSize);

        float fontSize  = Math.Max(7f, iconSize / 6f);
        _labelFont      = TryFont("Segoe UI", fontSize) ?? new Font(SystemFonts.DefaultFont.FontFamily, fontSize);
        Font            = _labelFont;

        int pad = Pad();
        switch (displayMode)
        {
            case "icons_only":
                Width  = iconSize + pad * 2;
                Height = iconSize + pad * 2;
                break;
            case "labels_only":
                Width  = 120;
                Height = (int)(_labelFont.Height * 1.6f) + pad * 2;
                break;
            default: // icons_labels
                Width  = iconSize + pad * 2;
                Height = iconSize + (int)(_labelFont.Height * 1.8f) + pad * 2;
                break;
        }

        Invalidate();
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Transparent);
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

        if (_pressed || _hovered)
        {
            // Windows 11 uses a very subtle, semi-transparent rounded-rect for hover
            var alpha   = _pressed ? 40 : 20;
            var fillClr = Color.FromArgb(alpha, SystemColors.WindowText);
            using var brush = new SolidBrush(fillClr);
            using var path  = RoundedRect(new Rectangle(2, 2, Width - 4, Height - 4), Radius);
            g.FillPath(brush, path);
        }

        int pad = Pad();
        switch (DisplayMode)
        {
            case "icons_only":
                DrawIcon(g, pad, pad, _iconSize);
                DrawRecencyDot(g, Width - 7, 3);
                break;
            case "labels_only":
                DrawLabel(g, pad, pad, Width - pad * 2, Height - pad * 2);
                DrawRecencyDot(g, Width - 7, Height / 2 - 3);
                break;
            default: // icons_labels
                DrawIcon(g, pad, pad, _iconSize);
                int labelY = pad + _iconSize + 2;
                DrawLabel(g, pad, labelY, Width - pad * 2, Height - labelY - pad);
                DrawRecencyDot(g, Width - 7, 3);
                break;
        }
    }

    private void DrawIcon(Graphics g, int x, int y, int size)
    {
        _icon ??= _iconService.GetIcon(EntryModel, size);
        if (_icon != null)
            g.DrawImage(_icon, x, y, size, size);
        else
        {
            // Fallback: draw a simple rounded placeholder
            using var br = new SolidBrush(Color.FromArgb(60, SystemColors.WindowText));
            g.FillRectangle(br, x, y, size, size);
        }
    }

    private void DrawLabel(Graphics g, int x, int y, int w, int h)
    {
        var fmt = new StringFormat
        {
            Trimming      = StringTrimming.EllipsisCharacter,
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags   = StringFormatFlags.LineLimit,
        };
        using var brush = new SolidBrush(ForeColor);
        g.DrawString(EntryModel.Name, _labelFont, brush,
                     new RectangleF(x, y, w, Math.Max(h, _labelFont.Height)), fmt);
    }

    private void DrawRecencyDot(Graphics g, int x, int y)
    {
        if (!LaunchService.IsRecent(EntryModel)) return;
        using var br = new SolidBrush(Color.FromArgb(200, 40, 180, 80));
        g.FillEllipse(br, x, y, 6, 6);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    private void WireEvents()
    {
        MouseEnter  += (_, _) => { _hovered = true;  Invalidate(); };
        MouseLeave  += (_, _) => { _hovered = false; _pressed = false; Invalidate(); };
        MouseDown   += (_, me) => { if (me.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } };
        MouseUp     += (_, me) => { if (me.Button == MouseButtons.Left) { _pressed = false; Invalidate(); } };

        Click += (_, _) =>
        {
            _launchService.Launch(EntryModel);
            Invalidate();
            LaunchRequested?.Invoke(this, EventArgs.Empty);
        };

        var tip = new ToolTip { ShowAlways = true };
        tip.SetToolTip(this, EntryModel.Name);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9f) };
        menu.Opening += (_, _) => RebuildMoveToMenu(menu);

        menu.Items.Add("Edit Entry",         null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Open File Location", null, (_, _) => OpenLocationRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove Entry",       null, OnRemoveClicked);

        ContextMenuStrip = menu;
    }

    private void RebuildMoveToMenu(ContextMenuStrip menu)
    {
        for (int i = menu.Items.Count - 1; i >= 0; i--)
            if (menu.Items[i].Text == "Move to Section") menu.Items.RemoveAt(i);

        var moveItem = new ToolStripMenuItem("Move to Section");
        foreach (var sec in _configService.Sections)
        {
            if (sec.Type == "scratchpad") continue;
            var captured = sec;
            moveItem.DropDownItems.Add(sec.Name, null,
                (_, _) => MoveToSectionRequested?.Invoke(this, captured));
        }
        menu.Items.Insert(menu.Items.Count - 2, moveItem);
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (MessageBox.Show($"Remove \"{EntryModel.Name}\"?", "Personal TaskBar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int Pad() => Math.Max(4, _iconSize / 10);

    private static Font? TryFont(string name, float size)
    {
        try { return new Font(name, size); } catch { return null; }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
