using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Custom panel control that renders one section: header bar with accent line,
/// collapse animation, and a flow container of EntryButton or ScratchpadPanel children.
/// Supports drag-to-reorder of entries within itself and drag-to-move between sections.
/// </summary>
public class SectionPanel : Panel
{
    // ── Data ────────────────────────────────────────────────────────────────

    public Section SectionModel { get; }

    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly ConfigService          _configService;
    private readonly IconService            _iconService;
    private readonly LaunchService          _launchService;
    private readonly Func<Form, DialogResult> _showDialog;

    // ── Child controls ──────────────────────────────────────────────────────

    private readonly Panel          _header;        // Section name + accent line
    private readonly FlowLayoutPanel _content;      // Entry buttons or scratchpad
    private readonly Label          _headerLabel;

    // ── State ───────────────────────────────────────────────────────────────

    private bool   _collapsed;
    private int    _iconSize    = 48;
    private string _displayMode = "icons_labels";

    // ── Drag-to-reorder state ───────────────────────────────────────────────

    private EntryButton? _draggedEntry;
    private int          _dragStartIndex;

    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler? DataChanged;          // raised whenever anything persists

    // ── Constructor ─────────────────────────────────────────────────────────

    public SectionPanel(Section section, ConfigService configService,
                        IconService iconService, LaunchService launchService,
                        Func<Form, DialogResult>? showDialogOverride = null)
    {
        SectionModel   = section;
        _configService = configService;
        _iconService   = iconService;
        _launchService = launchService;
        _collapsed     = section.Collapsed;
        // Fall back to a plain ShowDialog if no override is provided (e.g. in tests)
        _showDialog = showDialogOverride ?? (dlg => dlg.ShowDialog());

        BackColor    = SystemColors.Control;
        ForeColor    = SystemColors.ControlText;
        // Do NOT use AutoSize here – the FlowLayoutPanel host is TopDown, so each
        // SectionPanel must have an explicit width that fills the host.  Width is
        // set by the caller (MainForm) via SetWidth() whenever the form resizes.
        AutoSize     = false;
        Padding      = new Padding(0, 0, 0, 4);

        // ── Header panel ──────────────────────────────────────────────────

        _header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 24,
            BackColor = SystemColors.Control,
            Cursor    = Cursors.Hand,
        };

        _headerLabel = new Label
        {
            Text      = section.Name,
            Font      = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            ForeColor = SystemColors.ControlText,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(4, 0, 0, 0),
        };
        _header.Controls.Add(_headerLabel);

        // ── Content area (flow of entry buttons or scratchpad) ────────────

        _content = new FlowLayoutPanel
        {
            FlowDirection  = FlowDirection.LeftToRight,
            WrapContents   = true,
            AutoSize       = true,
            AutoSizeMode   = AutoSizeMode.GrowAndShrink,
            BackColor      = SystemColors.Control,
            Visible        = !_collapsed,
        };

        Controls.Add(_content);
        Controls.Add(_header); // header on top (DockStyle.Top processed last)

        BuildContextMenu();
        WireHeaderEvents();

        if (section.Type == "scratchpad")
            BuildScratchpad();
        else
            RebuildEntries();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MainForm on resize so the panel and its content fill the host width.
    /// </summary>
    public void SetWidth(int width)
    {
        Width          = Math.Max(80, width);
        _content.Width = Width;
        foreach (Control c in _content.Controls)
            if (c is ScratchpadPanel sp)
                sp.Width = Width;
        // Height: header + content natural height
        UpdateHeight();
    }

    private void UpdateHeight()
    {
        int contentH = _collapsed ? 0 : _content.PreferredSize.Height;
        Height = _header.Height + contentH + Padding.Vertical;
    }

    /// <summary>Propagates a new icon size to all child entry buttons.</summary>
    public void ApplyIconSize(int iconSize)
    {
        _iconSize = iconSize;

        // Scale header height proportionally
        _header.Height = Math.Max(18, iconSize / 2);
        _headerLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily,
                                      Math.Max(7, iconSize / 5),
                                      FontStyle.Bold);

        foreach (Control c in _content.Controls)
        {
            if (c is EntryButton eb)
                eb.ApplyIconSize(iconSize, _displayMode);
            else if (c is ScratchpadPanel sp)
                sp.ApplyIconSize(iconSize);
        }
    }

    /// <summary>Rebuilds or refreshes entry buttons after data changes.</summary>
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
        _displayMode                 = mode;
        SectionModel.DisplayMode     = mode;

        foreach (Control c in _content.Controls)
            if (c is EntryButton eb)
                eb.ApplyIconSize(_iconSize, mode);

        _configService.SaveEntries();
    }

    // ── Entry button factory ────────────────────────────────────────────────

    private EntryButton CreateEntryButton(Entry entry)
    {
        var btn = new EntryButton(entry, _iconService, _launchService, _configService)
        {
            Margin = new Padding(2),
        };

        btn.EditRequested       += (_, _) => OpenEditDialog(entry, btn);
        btn.RemoveRequested     += (_, _) => RemoveEntry(entry, btn);
        btn.OpenLocationRequested += (_, _) => LaunchService.OpenFileLocation(entry);
        btn.MoveToSectionRequested += (_, sec) => MoveEntryToSection(entry, btn, sec);

        // Drag-to-reorder support
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
    }

    // ── Header click → collapse/expand ─────────────────────────────────────

    private void WireHeaderEvents()
    {
        _header.Click += (_, _) => ToggleCollapse();
        _headerLabel.Click += (_, _) => ToggleCollapse();
    }

    private void ToggleCollapse()
    {
        _collapsed            = !_collapsed;
        SectionModel.Collapsed = _collapsed;

        // Simple show/hide animation: fade the content in/out
        _content.Visible = !_collapsed;
        _configService.SaveEntries();
        UpdateHeight();

        // Repaint the header arrow hint
        _headerLabel.Text = SectionModel.Name + (_collapsed ? " ▶" : " ▼");
        _header.Invalidate();
    }

    // ── Context menu ────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var displayMenu = new ToolStripMenuItem("Display Mode");
        displayMenu.DropDownItems.Add("Icons Only",        null, (_, _) => SetDisplayMode("icons_only"));
        displayMenu.DropDownItems.Add("Icons and Labels",  null, (_, _) => SetDisplayMode("icons_labels"));
        displayMenu.DropDownItems.Add("Labels Only",       null, (_, _) => SetDisplayMode("labels_only"));
        menu.Items.Add(displayMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Add Entry",          null, (_, _) => OpenAddEntryDialog());
        menu.Items.Add("Rename Section",     null, (_, _) => RenameSection());
        menu.Items.Add("Change Accent Color",null, (_, _) => ChangeAccentColor());
        menu.Items.Add("Convert to Scratchpad", null, (_, _) => ConvertToScratchpad());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Collapse All",       null, (_, _) => CollapseAll?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Expand All",         null, (_, _) => ExpandAll?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove Section",     null, OnRemoveSectionClicked);

        ContextMenuStrip       = menu;
        _header.ContextMenuStrip = menu;
        _headerLabel.ContextMenuStrip = menu;
    }

    public event EventHandler? CollapseAll;
    public event EventHandler? ExpandAll;
    public event EventHandler? RemoveSectionRequested;

    private void OnRemoveSectionClicked(object? sender, EventArgs e)
    {
        var r = MessageBox.Show($"Remove section \"{SectionModel.Name}\" and all its entries?",
                                "Personal TaskBar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes)
            RemoveSectionRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Painting (accent line) ──────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw the accent line at the bottom of the header band
        try
        {
            var color = ColorTranslator.FromHtml(SectionModel.AccentColor);
            using var pen = new Pen(color, 2);
            e.Graphics.DrawLine(pen, 0, _header.Bottom - 2, Width, _header.Bottom - 2);
        }
        catch { /* ignore invalid colour strings */ }
    }

    // ── Entry CRUD helpers ──────────────────────────────────────────────────

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
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Section rename / colour ─────────────────────────────────────────────

    private void RenameSection()
    {
        using var dlg = new Form
        {
            Text            = "Rename Section",
            Size            = new Size(300, 120),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false, MinimizeBox = false,
        };
        var tb = new TextBox { Text = SectionModel.Name, Dock = DockStyle.Top, Margin = new Padding(8) };
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

    // ── Drag-to-reorder entries ─────────────────────────────────────────────

    private void OnEntryMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not EntryButton btn) return;
        _draggedEntry   = btn;
        _dragStartIndex = _content.Controls.IndexOf(btn);
    }

    private void OnEntryMouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggedEntry == null || e.Button != MouseButtons.Left) return;

        // Determine which slot the mouse is over and swap
        var pos      = _content.PointToClient(MousePosition);
        var target   = _content.GetChildAtPoint(pos) as EntryButton;
        if (target == null || target == _draggedEntry) return;

        int from = _content.Controls.IndexOf(_draggedEntry);
        int to   = _content.Controls.IndexOf(target);
        if (from == to) return;

        _content.Controls.SetChildIndex(_draggedEntry, to);

        // Mirror reorder in the model
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

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Color TryParseColor(string hex)
    {
        try   { return ColorTranslator.FromHtml(hex); }
        catch { return Color.CornflowerBlue; }
    }
}
