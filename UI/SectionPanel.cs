using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Custom panel representing one named section of the bar.
///
/// Layout (top → bottom):
///   ┌──────────────────────────────────┐
///   │  _header  (Panel, Dock=Top)      │  fixed height
///   ├──────────────────────────────────┤
///   │  accent line (painted in OnPaint)│  2 px
///   ├──────────────────────────────────┤
///   │  _content (FlowLayoutPanel)      │  grows with entries
///   └──────────────────────────────────┘
///
/// Key invariant: _content.Location.Y == _header.Height + 2 always.
/// UpdateHeight() recalculates the SectionPanel's own Height to fit both.
/// </summary>
public class SectionPanel : Panel
{
    // ── Data ────────────────────────────────────────────────────────────────

    public Section SectionModel { get; }

    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly ConfigService              _configService;
    private readonly IconService                _iconService;
    private readonly LaunchService              _launchService;
    private readonly Func<Form, DialogResult>   _showDialog;

    // ── Child controls ──────────────────────────────────────────────────────

    private readonly Panel           _header;
    private readonly Label           _headerLabel;
    private readonly FlowLayoutPanel _content;

    // ── State ────────────────────────────────────────────────────────────────

    private bool   _collapsed;
    private int    _iconSize    = 48;
    private string _displayMode = "icons_labels";

    // ── Drag-to-reorder ──────────────────────────────────────────────────────

    private EntryButton? _draggedEntry;

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler? DataChanged;
    public event EventHandler? CollapseAll;
    public event EventHandler? ExpandAll;
    public event EventHandler? RemoveSectionRequested;

    // Accent line height below the header
    private const int AccentH = 2;

    // ── Constructor ──────────────────────────────────────────────────────────

    public SectionPanel(Section section, ConfigService configService,
                        IconService iconService, LaunchService launchService,
                        Func<Form, DialogResult>? showDialogOverride = null)
    {
        SectionModel   = section;
        _configService = configService;
        _iconService   = iconService;
        _launchService = launchService;
        _collapsed     = section.Collapsed;
        _showDialog    = showDialogOverride ?? (d => d.ShowDialog());

        BackColor    = SystemColors.Window;
        ForeColor    = SystemColors.WindowText;
        AutoSize     = false;
        Padding      = new Padding(0, 0, 0, 6);

        // ── Header ───────────────────────────────────────────────────────

        _header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 24,
            BackColor = Color.Transparent,
            Cursor    = Cursors.SizeAll,   // hint that the section is draggable
        };

        _headerLabel = new Label
        {
            Text      = section.Name + (_collapsed ? " ▶" : " ▼"),
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Bold),
            ForeColor = SystemColors.WindowText,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(6, 0, 0, 0),
            Cursor    = Cursors.Hand,
        };
        _header.Controls.Add(_headerLabel);

        // ── Content (entries or scratchpad) ──────────────────────────────
        // Location is managed manually: always at (0, header.Height + AccentH)
        // so it sits below the header and accent line.
        // AutoSize=false; height is calculated by UpdateHeight().

        _content = new FlowLayoutPanel
        {
            Location      = new Point(0, _header.Height + AccentH),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = true,
            AutoSize      = false,
            Height        = 0,
            BackColor     = Color.Transparent,
            Visible       = !_collapsed,
        };

        // Add header first (DockStyle.Top), then content below
        Controls.Add(_header);
        Controls.Add(_content);

        BuildContextMenu();
        WireHeaderEvents();

        if (section.Type == "scratchpad")
            BuildScratchpad();
        else
            RebuildEntries();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the panel width and repositions/resizes all children to match.
    /// Called by MainForm whenever the form is resized or sections are rebuilt.
    /// </summary>
    public void SetWidth(int width)
    {
        width          = Math.Max(80, width);
        Width          = width;
        _content.Width = width;
        foreach (Control c in _content.Controls)
            if (c is ScratchpadPanel sp) sp.Width = width;
        UpdateHeight();
    }

    /// <summary>Propagates a new icon size to all child controls.</summary>
    public void ApplyIconSize(int iconSize)
    {
        _iconSize      = iconSize;
        _header.Height = Math.Max(20, iconSize / 2);
        _headerLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily,
                                      Math.Max(8f, iconSize / 6f), FontStyle.Bold);
        _content.Location = new Point(0, _header.Height + AccentH);

        foreach (Control c in _content.Controls)
        {
            if (c is EntryButton eb) eb.ApplyIconSize(iconSize, _displayMode);
            else if (c is ScratchpadPanel sp) sp.ApplyIconSize(iconSize);
        }
        UpdateHeight();
    }

    /// <summary>Rebuilds entry buttons from the model (called after search filter).</summary>
    public void RebuildEntries()
    {
        _content.SuspendLayout();
        _content.Controls.Clear();

        foreach (var entry in SectionModel.Entries)
        {
            var btn = CreateEntryButton(entry);
            btn.ApplyIconSize(_iconSize, _displayMode);
            _content.Controls.Add(btn);
        }

        _content.ResumeLayout(true);
        UpdateHeight();
    }

    public void SetDisplayMode(string mode)
    {
        _displayMode         = mode;
        SectionModel.DisplayMode = mode;
        foreach (Control c in _content.Controls)
            if (c is EntryButton eb) eb.ApplyIconSize(_iconSize, mode);
        _configService.SaveEntries();
        UpdateHeight();
    }

    // ── Height management ────────────────────────────────────────────────────

    private void UpdateHeight()
    {
        _content.Location = new Point(0, _header.Height + AccentH);

        if (_collapsed)
        {
            _content.Visible = false;
            Height = _header.Height + AccentH + Padding.Vertical;
            return;
        }

        _content.Visible = true;

        // Measure content height from the bottom of the lowest child control
        int contentH = 0;
        foreach (Control c in _content.Controls)
            contentH = Math.Max(contentH, c.Bottom + c.Margin.Bottom);

        // If the section is empty, show a small placeholder so the user can
        // right-click it to add entries
        contentH = Math.Max(contentH, 24);
        _content.Height = contentH;

        Height = _header.Height + AccentH + contentH + Padding.Vertical;
    }

    // ── Painting (accent line) ────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw a 2px coloured accent line immediately below the header
        int y = _header.Height;
        try
        {
            using var pen = new Pen(ColorTranslator.FromHtml(SectionModel.AccentColor), AccentH);
            e.Graphics.DrawLine(pen, 0, y, Width, y);
        }
        catch { /* ignore invalid colour */ }
    }

    // ── Entry button factory ─────────────────────────────────────────────────

    private EntryButton CreateEntryButton(Entry entry)
    {
        var btn = new EntryButton(entry, _iconService, _launchService, _configService)
        {
            Margin = new Padding(2),
        };
        btn.EditRequested             += (_, _) => OpenEditDialog(entry, btn);
        btn.RemoveRequested           += (_, _) => RemoveEntry(entry, btn);
        btn.OpenLocationRequested     += (_, _) => LaunchService.OpenFileLocation(entry);
        btn.MoveToSectionRequested    += (_, sec) => MoveEntryToSection(entry, btn, sec);
        btn.MouseDown += OnEntryMouseDown;
        btn.MouseMove += OnEntryMouseMove;
        btn.MouseUp   += OnEntryMouseUp;
        return btn;
    }

    private void BuildScratchpad()
    {
        _content.Controls.Clear();
        var sp = new ScratchpadPanel(SectionModel, _configService)
        {
            Width = Width > 0 ? Width : 300,
        };
        _content.Controls.Add(sp);
        UpdateHeight();
    }

    // ── Header: collapse / expand ────────────────────────────────────────────

    private void WireHeaderEvents()
    {
        _header.Click      += (_, _) => ToggleCollapse();
        _headerLabel.Click += (_, _) => ToggleCollapse();
    }

    private void ToggleCollapse()
    {
        _collapsed              = !_collapsed;
        SectionModel.Collapsed  = _collapsed;
        _headerLabel.Text       = SectionModel.Name + (_collapsed ? " ▶" : " ▼");
        _configService.SaveEntries();
        UpdateHeight();
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9f),
        };

        var dispMenu = new ToolStripMenuItem("Display Mode");
        dispMenu.DropDownItems.Add("Icons Only",       null, (_, _) => SetDisplayMode("icons_only"));
        dispMenu.DropDownItems.Add("Icons and Labels", null, (_, _) => SetDisplayMode("icons_labels"));
        dispMenu.DropDownItems.Add("Labels Only",      null, (_, _) => SetDisplayMode("labels_only"));
        menu.Items.Add(dispMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Add Entry",           null, (_, _) => OpenAddEntryDialog());
        menu.Items.Add("Rename Section",      null, (_, _) => RenameSection());
        menu.Items.Add("Change Accent Color", null, (_, _) => ChangeAccentColor());
        menu.Items.Add("Convert to Scratchpad", null, (_, _) => ConvertToScratchpad());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Collapse All", null, (_, _) => CollapseAll?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Expand All",   null, (_, _) => ExpandAll?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove Section", null, OnRemoveSectionClicked);

        ContextMenuStrip          = menu;
        _header.ContextMenuStrip  = menu;
        _headerLabel.ContextMenuStrip = menu;
    }

    private void OnRemoveSectionClicked(object? sender, EventArgs e)
    {
        var r = MessageBox.Show(
            $"Remove \"{SectionModel.Name}\" and all its entries?",
            "Personal TaskBar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes)
            RemoveSectionRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Entry CRUD ───────────────────────────────────────────────────────────

    private void OpenAddEntryDialog()
    {
        var newEntry = new Entry();
        using var dlg = new EditEntryForm(newEntry, isNew: true);
        if (_showDialog(dlg) == DialogResult.OK)
        {
            SectionModel.Entries.Add(newEntry);
            _configService.SaveEntries();
            RebuildEntries();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenEditDialog(Entry entry, EntryButton btn)
    {
        using var dlg = new EditEntryForm(entry, isNew: false);
        if (_showDialog(dlg) == DialogResult.OK)
        {
            _configService.SaveEntries();
            btn.Invalidate();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RemoveEntry(Entry entry, EntryButton btn)
    {
        SectionModel.Entries.Remove(entry);
        _content.Controls.Remove(btn);
        btn.Dispose();
        _configService.SaveEntries();
        UpdateHeight();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveEntryToSection(Entry entry, EntryButton btn, Section target)
    {
        if (target == SectionModel) return;
        SectionModel.Entries.Remove(entry);
        target.Entries.Add(entry);
        _content.Controls.Remove(btn);
        btn.Dispose();
        _configService.SaveEntries();
        UpdateHeight();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Section rename / colour ──────────────────────────────────────────────

    private void RenameSection()
    {
        using var dlg = new Form
        {
            Text = "Rename Section", Size = new Size(300, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            Font = new Font("Segoe UI", 9f),
        };
        var tb = new TextBox { Text = SectionModel.Name, Dock = DockStyle.Top };
        var ok = new Button  { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dlg.Controls.AddRange(new Control[] { ok, tb });
        dlg.AcceptButton = ok;

        if (_showDialog(dlg) == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text))
        {
            SectionModel.Name   = tb.Text.Trim();
            _headerLabel.Text   = SectionModel.Name + (_collapsed ? " ▶" : " ▼");
            _configService.SaveEntries();
        }
    }

    private void ChangeAccentColor()
    {
        using var dlg = new ColorDialog { Color = TryParseColor(SectionModel.AccentColor) };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            SectionModel.AccentColor = ColorTranslator.ToHtml(dlg.Color);
            _configService.SaveEntries();
            Invalidate();
        }
    }

    private void ConvertToScratchpad()
    {
        var r = MessageBox.Show(
            "Convert to Scratchpad? All entries in this section will be removed.",
            "Personal TaskBar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;

        SectionModel.Type = "scratchpad";
        SectionModel.Entries.Clear();
        _content.Controls.Clear();
        BuildScratchpad();
        _configService.SaveEntries();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Drag-to-reorder entries ──────────────────────────────────────────────

    private void OnEntryMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not EntryButton btn) return;
        _draggedEntry = btn;
    }

    private void OnEntryMouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggedEntry == null || e.Button != MouseButtons.Left) return;
        var pos    = _content.PointToClient(MousePosition);
        var target = _content.GetChildAtPoint(pos) as EntryButton;
        if (target == null || target == _draggedEntry) return;

        int from = _content.Controls.IndexOf(_draggedEntry);
        int to   = _content.Controls.IndexOf(target);
        if (from == to) return;

        _content.Controls.SetChildIndex(_draggedEntry, to);
        var entries = SectionModel.Entries;
        var item    = entries[from];
        entries.RemoveAt(from);
        entries.Insert(to, item);
    }

    private void OnEntryMouseUp(object? sender, MouseEventArgs e)
    {
        if (_draggedEntry == null) return;
        _configService.SaveEntries();
        _draggedEntry = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Color TryParseColor(string hex)
    {
        try   { return ColorTranslator.FromHtml(hex); }
        catch { return Color.CornflowerBlue; }
    }
}
