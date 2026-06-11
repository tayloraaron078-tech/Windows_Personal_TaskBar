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

    // ── Dock / resize state ─────────────────────────────────────────────────

    private const int DockSnapThreshold = 20; // pixels from screen edge to trigger snap
    private const int ResizeBorder      = 6;  // pixels from form edge that count as resize grip

    // Default floating size used when undocking from a full-edge dock
    private Size DefaultFloatingSize => new(Math.Max(200, _configService.Config.Window.IconSize * 5),
                                            Math.Max(80,  _configService.Config.Window.IconSize * 2));

    // System tray icon – gives user a way to show/exit when the window is hidden
    private NotifyIcon? _trayIcon;

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainForm(ConfigService configService)
    {
        _configService = configService;
        _iconService   = new IconService();
        _launchService = new LaunchService(configService);

        // ── Window setup ─────────────────────────────────────────────────

        FormBorderStyle = FormBorderStyle.None;
        TopMost         = configService.Config.Window.AlwaysOnTop;
        ShowInTaskbar   = true;  // visible in taskbar so user can find/click the bar
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
        // TopDown flow so sections stack vertically regardless of form width.
        // AutoScroll on the outer form handles overflow when content is taller than the window.

        _sectionsHost = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoSize      = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
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

        // ── System tray icon ──────────────────────────────────────────────
        // Provides Show/Hide and Exit even when the main window is hidden.

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show / Hide",  null, (_, _) => ToggleVisibility());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon             = SystemIcons.Application,
            Text             = "Personal TaskBar",
            Visible          = true,
            ContextMenuStrip = trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();

        // ── Build section panels ──────────────────────────────────────────

        RebuildSections();

        // Preload icons at startup so first render is instant
        _iconService.Preload(_configService.Sections, _configService.Config.Window.IconSize);
    }

    // ── WndProc override (hotkeys + resize + single-instance activation) ────

    // WM_NCHITTEST return values for resize edges
    private const int WM_NCHITTEST   = 0x0084;
    private const int HTLEFT         = 10;
    private const int HTRIGHT        = 11;
    private const int HTTOP          = 12;
    private const int HTTOPLEFT      = 13;
    private const int HTTOPRIGHT     = 14;
    private const int HTBOTTOM       = 15;
    private const int HTBOTTOMLEFT   = 16;
    private const int HTBOTTOMRIGHT  = 17;
    private const int HTCLIENT       = 1;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            // Decode screen coords – use signed shorts to handle negative values on
            // monitors to the left/above the primary.
            int  lp     = m.LParam.ToInt32();
            var  screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var  cursor = PointToClient(screen);

            bool left   = cursor.X <= ResizeBorder;
            bool right  = cursor.X >= ClientSize.Width  - ResizeBorder;
            bool top    = cursor.Y <= ResizeBorder;
            bool bottom = cursor.Y >= ClientSize.Height - ResizeBorder;

            var dock = _configService?.Config.Window.Dock ?? "none";

            // Suppress resize on edges that are pinned to the screen
            if (dock == "top")    top    = false;
            if (dock == "bottom") bottom = false;
            if (dock == "left")   left   = false;
            if (dock == "right")  right  = false;

            int hit = HTCLIENT;
            if (top    && left)   hit = HTTOPLEFT;
            else if (top  && right)  hit = HTTOPRIGHT;
            else if (bottom && left)  hit = HTBOTTOMLEFT;
            else if (bottom && right) hit = HTBOTTOMRIGHT;
            else if (left)   hit = HTLEFT;
            else if (right)  hit = HTRIGHT;
            else if (top)    hit = HTTOP;
            else if (bottom) hit = HTBOTTOM;

            if (hit != HTCLIENT) { m.Result = new IntPtr(hit); return; }

            // If the cursor is not over a resize edge and not over an interactive
            // control, tell Windows this is the title bar. That gives us free native
            // drag-to-move AND the snap-on-release via WM_MOVING without any manual
            // MouseMove tracking.
            if (!IsInteractiveControlAt(cursor))
            {
                m.Result = new IntPtr(HTCAPTION);
                return;
            }
        }

        // WM_MOVING fires continuously while the user is dragging; use it to snap
        // the window to screen edges the moment the mouse button is released.
        const int WM_EXITSIZEMOVE = 0x0232;
        if (m.Msg == WM_EXITSIZEMOVE)
        {
            OnMoveOrResizeEnded();
        }

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

        // Clamp saved size so a stale full-screen value doesn't make the bar unusable.
        // Docked positions override these anyway via ApplyDock below.
        var wa    = screen.WorkingArea;
        int safeW = wc.Dock is "top" or "bottom" ? wa.Width  : Math.Clamp(wc.Width,  150, wa.Width  / 2);
        int safeH = wc.Dock is "left" or "right"  ? wa.Height : Math.Clamp(wc.Height, 50,  wa.Height / 2);
        Size = new Size(safeW, safeH);

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

    // ── Native-drag helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if there is an interactive control (button, textbox, etc.)
    /// at the given client-coordinate point.  Used by WM_NCHITTEST to decide
    /// whether to return HTCAPTION (drag) or HTCLIENT (normal input).
    /// </summary>
    private bool IsInteractiveControlAt(Point clientPt)
    {
        // Walk the control tree from this form down to the deepest child
        Control current = this;
        while (true)
        {
            var child = current.GetChildAtPoint(
                current == this ? clientPt : current.PointToClient(PointToScreen(clientPt)));
            if (child == null) break;
            if (child is Button or TextBox or RichTextBox or TrackBar
                      or ComboBox or CheckBox or RadioButton or ScrollBar)
                return true;
            current = child;
        }
        return false;
    }

    /// <summary>
    /// Called when the user finishes a move or resize (WM_EXITSIZEMOVE).
    /// Handles snap-to-dock and saves geometry.
    /// </summary>
    private void OnMoveOrResizeEnded()
    {
        var prevDock = _configService.Config.Window.Dock;

        // Reset full-edge-dock size when dragging away from a docked position
        if (prevDock is "top" or "bottom")
            Width = DefaultFloatingSize.Width;
        else if (prevDock is "left" or "right")
            Height = DefaultFloatingSize.Height;

        _configService.Config.Window.Dock = "none";
        TrySnap();
        SaveWindowGeometry();
    }

    // ── Resize override ─────────────────────────────────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Guard: OnResize fires during RestoreWindowGeometry() which is called
        // before _sectionsHost and _sectionPanels are assigned.
        if (_sectionsHost == null || _sectionPanels == null) return;

        int w = SectionWidth();
        foreach (var sp in _sectionPanels)
            sp.SetWidth(w);
        SaveWindowGeometry();
    }

    /// <summary>Width available for section panels inside the sections host.</summary>
    private int SectionWidth() =>
        Math.Max(80, _sectionsHost.ClientSize.Width
                     - (_sectionsHost.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));

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
        panel.SetWidth(SectionWidth());

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
        int w = SectionWidth();
        foreach (var sp in _sectionPanels)
        {
            sp.ApplyIconSize(size);
            sp.SetWidth(w);
        }
        _configService.SaveConfig();
    }

    // ── Exit ────────────────────────────────────────────────────────────────

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        _hotkeyService?.Dispose();
        _iconService.Dispose();
        Application.Exit();
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Intercept the close button (Alt+F4, taskbar close) and hide instead,
        // unless we're doing a real exit via ExitApp().
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon?.Dispose();
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
