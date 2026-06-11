using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;
using Personal_TaskBar.UI;

namespace Personal_TaskBar;

/// <summary>
/// The main application window.
/// Borderless, always-on-top floating toolbar that can dock to any screen edge.
/// All window behaviour (drag, snap-to-dock, resize, multi-monitor) lives here.
/// </summary>
public class MainForm : Form
{
    // ── Services ────────────────────────────────────────────────────────────

    private readonly ConfigService  _configService;
    private readonly IconService    _iconService;
    private readonly LaunchService  _launchService;
    private          HotkeyService? _hotkeyService;

    // ── Hotkey IDs ──────────────────────────────────────────────────────────

    private int _hotToggleId = -1;
    private int _hotSearchId = -1;

    // ── UI ──────────────────────────────────────────────────────────────────

    private readonly Panel            _controlStrip;   // lock/gear/hide buttons
    private readonly FlowLayoutPanel  _sectionsHost;   // houses all SectionPanels
    private readonly SearchOverlay    _searchOverlay;
    private readonly List<SectionPanel> _sectionPanels = new();

    // Control-strip buttons
    private readonly Button _btnAlwaysOnTop;
    private readonly Button _btnSettings;
    private readonly Button _btnHide;
    private readonly Button _btnSearch;
    private readonly Button _btnAddSection;

    // ── Drag / dock state ───────────────────────────────────────────────────

    private const int DockSnapThreshold = 20; // pixels from edge to trigger snap

    private bool  _dragging;
    private Point _dragOrigin;            // mouse position when drag started
    private Point _formOrigin;            // form position when drag started

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainForm(ConfigService configService)
    {
        _configService = configService;
        _iconService   = new IconService();
        _launchService = new LaunchService(configService);

        // ── Window setup ─────────────────────────────────────────────────

        FormBorderStyle = FormBorderStyle.None;
        TopMost         = configService.Config.Window.AlwaysOnTop;
        ShowInTaskbar   = false;
        DoubleBuffered  = true;
        BackColor       = SystemColors.Control;
        ForeColor       = SystemColors.ControlText;
        Text            = "Personal TaskBar";

        RestoreWindowGeometry();

        // ── Control strip (always-visible band of utility buttons) ────────

        _btnAlwaysOnTop = MakeStripButton("🔒", "Toggle always-on-top");
        _btnSettings    = MakeStripButton("⚙",  "Settings");
        _btnHide        = MakeStripButton("✕",  "Hide bar");
        _btnSearch      = MakeStripButton("🔍",  "Search (Ctrl+Space)");
        _btnAddSection  = MakeStripButton("+",  "Add section");

        _controlStrip = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 24,
            BackColor = SystemColors.ControlDark,
        };

        var stripFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoSize      = false,
            BackColor     = SystemColors.ControlDark,
        };
        stripFlow.Controls.AddRange(new Control[]
            { _btnAlwaysOnTop, _btnSearch, _btnAddSection, _btnSettings, _btnHide });
        _controlStrip.Controls.Add(stripFlow);

        // ── Search overlay (hidden by default) ────────────────────────────

        _searchOverlay              = new SearchOverlay(_configService, _launchService);
        _searchOverlay.Visible      = false;
        _searchOverlay.CloseRequested  += (_, _) => CloseSearch();
        _searchOverlay.FilterChanged   += OnSearchFilter;

        // ── Sections host ─────────────────────────────────────────────────

        _sectionsHost = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            WrapContents  = true,
            BackColor     = SystemColors.Control,
        };

        // Add controls from top to bottom
        Controls.Add(_sectionsHost);
        Controls.Add(_searchOverlay);
        Controls.Add(_controlStrip);

        // ── Wire strip buttons ────────────────────────────────────────────

        _btnAlwaysOnTop.Click += (_, _) => ToggleAlwaysOnTop();
        _btnSettings.Click    += (_, _) => OpenSettings();
        _btnHide.Click        += (_, _) => Hide();
        _btnSearch.Click      += (_, _) => OpenSearch();
        _btnAddSection.Click  += (_, _) => AddSection();

        // Dragging by the control strip
        _controlStrip.MouseDown += OnDragMouseDown;
        _controlStrip.MouseMove += OnDragMouseMove;
        _controlStrip.MouseUp   += OnDragMouseUp;

        // Dragging by the sections area (empty space)
        _sectionsHost.MouseDown += OnDragMouseDown;
        _sectionsHost.MouseMove += OnDragMouseMove;
        _sectionsHost.MouseUp   += OnDragMouseUp;

        // ── Build section panels ──────────────────────────────────────────

        RebuildSections();

        // Preload icons at startup so first render is instant
        _iconService.Preload(_configService.Sections, _configService.Config.Window.IconSize);
    }

    // ── WndProc override (hotkeys + single-instance activation) ────────────

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            _hotkeyService?.HandleHotkey(m.WParam.ToInt32());
        }
        else if (m.Msg == (int)NativeMethods.WM_ACTIVATE_INSTANCE)
        {
            // A second instance broadcast this to bring us forward
            if (!Visible) Show();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(Handle);
        }

        base.WndProc(ref m);
    }

    // ── Handle creation → register hotkeys ─────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService(Handle);

        var cfg = _configService.Config.Hotkeys;

        // Toggle visibility hotkey
        if (HotkeyService.TryParse(cfg.ToggleVisibility, out var mods, out var vk))
        {
            try { _hotToggleId = _hotkeyService.Register(mods, vk, ToggleVisibility); }
            catch { /* key in use – silently ignore */ }
        }

        // Search hotkey
        if (HotkeyService.TryParse(cfg.Search, out mods, out vk))
        {
            try { _hotSearchId = _hotkeyService.Register(mods, vk, OpenSearch); }
            catch { /* key in use */ }
        }
    }

    // ── Window geometry ─────────────────────────────────────────────────────

    private void RestoreWindowGeometry()
    {
        var wc = _configService.Config.Window;

        // Validate that the saved monitor index is still present
        var screens = Screen.AllScreens;
        var screen  = wc.Monitor < screens.Length ? screens[wc.Monitor] : Screen.PrimaryScreen!;

        // Clamp position to the target screen
        int x = Math.Clamp(wc.X, screen.WorkingArea.Left, screen.WorkingArea.Right  - wc.Width);
        int y = Math.Clamp(wc.Y, screen.WorkingArea.Top,  screen.WorkingArea.Bottom - wc.Height);

        Location = new Point(x, y);
        Size     = new Size(Math.Max(150, wc.Width), Math.Max(50, wc.Height));

        // Re-apply dock if previously docked
        if (wc.Dock != "none")
            ApplyDock(wc.Dock, screen);
    }

    private void SaveWindowGeometry()
    {
        var wc   = _configService.Config.Window;
        wc.X     = Location.X;
        wc.Y     = Location.Y;
        wc.Width = Width;
        wc.Height= Height;

        // Identify which monitor the window is on
        var screen = Screen.FromControl(this);
        wc.Monitor = Array.IndexOf(Screen.AllScreens, screen);
        if (wc.Monitor < 0) wc.Monitor = 0;

        _configService.SaveConfig();
    }

    // ── Dock logic ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the window is close enough to a screen edge to snap to it.
    /// Called at the end of every drag operation.
    /// </summary>
    private void TrySnap()
    {
        var screen = Screen.FromControl(this);
        var wa     = screen.WorkingArea;
        var loc    = Location;
        var sz     = Size;

        string dock = "none";

        if (loc.Y - wa.Top <= DockSnapThreshold)              dock = "top";
        else if (wa.Bottom - (loc.Y + sz.Height) <= DockSnapThreshold) dock = "bottom";
        else if (loc.X - wa.Left <= DockSnapThreshold)        dock = "left";
        else if (wa.Right - (loc.X + sz.Width) <= DockSnapThreshold)   dock = "right";

        _configService.Config.Window.Dock = dock;

        if (dock != "none")
            ApplyDock(dock, screen);
    }

    private void ApplyDock(string dock, Screen screen)
    {
        var wa = screen.WorkingArea;

        switch (dock)
        {
            case "top":
                Location = new Point(wa.Left, wa.Top);
                Width    = wa.Width;
                break;
            case "bottom":
                Location = new Point(wa.Left, wa.Bottom - Height);
                Width    = wa.Width;
                break;
            case "left":
                Location = new Point(wa.Left, wa.Top);
                Height   = wa.Height;
                break;
            case "right":
                Location = new Point(wa.Right - Width, wa.Top);
                Height   = wa.Height;
                break;
        }
    }

    // ── Drag mouse handlers ─────────────────────────────────────────────────

    private void OnDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging   = true;
        _dragOrigin = Control.MousePosition;
        _formOrigin = Location;
        // Undock when dragging starts
        _configService.Config.Window.Dock = "none";
    }

    private void OnDragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var delta = new Point(
            Control.MousePosition.X - _dragOrigin.X,
            Control.MousePosition.Y - _dragOrigin.Y);
        Location = new Point(_formOrigin.X + delta.X, _formOrigin.Y + delta.Y);
    }

    private void OnDragMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TrySnap();
        SaveWindowGeometry();
    }

    // ── Resize override ─────────────────────────────────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SaveWindowGeometry();
    }

    // ── Sections ────────────────────────────────────────────────────────────

    private void RebuildSections()
    {
        _sectionsHost.SuspendLayout();

        foreach (var sp in _sectionPanels)
            sp.Dispose();
        _sectionPanels.Clear();
        _sectionsHost.Controls.Clear();

        foreach (var section in _configService.Sections)
        {
            var panel = CreateSectionPanel(section);
            _sectionPanels.Add(panel);
            _sectionsHost.Controls.Add(panel);
        }

        _sectionsHost.ResumeLayout(true);
    }

    private SectionPanel CreateSectionPanel(Section section)
    {
        var panel = new SectionPanel(section, _configService, _iconService, _launchService, ShowDialogSafe);
        panel.ApplyIconSize(_configService.Config.Window.IconSize);

        panel.CollapseAll            += (_, _) => CollapseAllSections();
        panel.ExpandAll              += (_, _) => ExpandAllSections();
        panel.RemoveSectionRequested += (_, _) =>
        {
            _configService.Sections.Remove(section);
            _configService.SaveEntries();
            RebuildSections();
        };
        panel.DataChanged += (_, _) => { /* sections save themselves */ };

        return panel;
    }

    private void AddSection()
    {
        using var dlg = new Form
        {
            Text            = "Add Section",
            Size            = new Size(280, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false, MinimizeBox = false,
        };
        var tb = new TextBox { Text = "New Section", Dock = DockStyle.Top, Margin = new Padding(8) };
        var ok = new Button  { Text = "Add", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dlg.Controls.AddRange(new Control[] { ok, tb });
        dlg.AcceptButton = ok;

        if (ShowDialogSafe(dlg) != DialogResult.OK || string.IsNullOrWhiteSpace(tb.Text))
            return;

        var newSection = new Section { Name = tb.Text.Trim() };
        _configService.Sections.Add(newSection);
        _configService.SaveEntries();

        var panel = CreateSectionPanel(newSection);
        _sectionPanels.Add(panel);
        _sectionsHost.Controls.Add(panel);
    }

    // ── Collapse / expand all ───────────────────────────────────────────────

    private void CollapseAllSections()
    {
        foreach (var sp in _sectionPanels)
            sp.SectionModel.Collapsed = true;
        _configService.SaveEntries();
        RebuildSections();
    }

    private void ExpandAllSections()
    {
        foreach (var sp in _sectionPanels)
            sp.SectionModel.Collapsed = false;
        _configService.SaveEntries();
        RebuildSections();
    }

    // ── Dialog helper (suspend TopMost so dialogs aren't hidden behind the bar) ──

    /// <summary>
    /// Shows a dialog while temporarily suspending TopMost so the dialog is not
    /// covered by the always-on-top main window.  TopMost is restored afterwards.
    /// </summary>
    public DialogResult ShowDialogSafe(Form dlg)
    {
        bool wasTopMost = TopMost;
        TopMost = false;
        try     { return dlg.ShowDialog(this); }
        finally { TopMost = wasTopMost; }
    }

    // ── Always-on-top toggle ─────────────────────────────────────────────────

    private void ToggleAlwaysOnTop()
    {
        TopMost = !TopMost;
        _configService.Config.Window.AlwaysOnTop = TopMost;
        _btnAlwaysOnTop.Text = TopMost ? "🔒" : "🔓";
        _configService.SaveConfig();
    }

    // ── Visibility toggle (hotkey) ───────────────────────────────────────────

    private void ToggleVisibility()
    {
        if (Visible) Hide();
        else
        {
            Show();
            NativeMethods.SetForegroundWindow(Handle);
        }
    }

    // ── Settings ────────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_configService);

        dlg.IconSizeChanged   += (_, size) => ApplyIconSize(size);
        dlg.AlwaysOnTopChanged += (_, top) => { TopMost = top; };
        dlg.HotkeysChanged    += (_, _)    => RegisterHotkeys();

        ShowDialogSafe(dlg);
    }

    // ── Search ──────────────────────────────────────────────────────────────

    private void OpenSearch()
    {
        if (!Visible) { Show(); NativeMethods.SetForegroundWindow(Handle); }
        _searchOverlay.Activate();
    }

    private void CloseSearch()
    {
        _searchOverlay.Deactivate();
        // Restore all sections
        foreach (var sp in _sectionPanels)
        {
            sp.Visible = true;
            sp.RebuildEntries();
        }
    }

    private void OnSearchFilter(object? sender, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var sp in _sectionPanels)
                sp.Visible = true;
            return;
        }

        foreach (var sp in _sectionPanels)
        {
            bool anyMatch = sp.SectionModel.Entries
                .Any(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            sp.Visible = anyMatch;
        }
    }

    // ── Icon size propagation ───────────────────────────────────────────────

    private void ApplyIconSize(int size)
    {
        _configService.Config.Window.IconSize = size;
        foreach (var sp in _sectionPanels)
            sp.ApplyIconSize(size);
        _configService.SaveConfig();
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _hotkeyService?.Dispose();
        _iconService.Dispose();
        base.OnFormClosing(e);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Button MakeStripButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text      = text,
            Width     = 28,
            Height    = 22,
            FlatStyle = FlatStyle.Flat,
            BackColor = SystemColors.ControlDark,
            ForeColor = SystemColors.ControlText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 8),
            Margin    = new Padding(1),
            Padding   = Padding.Empty,
        };
        btn.FlatAppearance.BorderSize = 0;
        new ToolTip().SetToolTip(btn, tooltip);
        return btn;
    }
}
