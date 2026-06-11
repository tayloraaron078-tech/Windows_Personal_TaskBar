using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;
using Personal_TaskBar.UI;

namespace Personal_TaskBar;

/// <summary>
/// Main application window – borderless, always-on-top floating toolbar.
/// Drag is handled via the reliable ReleaseCapture+SendMessage(WM_NCLBUTTONDOWN)
/// pattern so that every empty surface on the bar can initiate a native OS move.
/// </summary>
public class MainForm : Form
{
    // ── Services ────────────────────────────────────────────────────────────

    private readonly ConfigService    _configService;
    private readonly IconService      _iconService;
    private readonly LaunchService    _launchService;
    private          HotkeyService?   _hotkeyService;

    // ── Hotkey IDs ──────────────────────────────────────────────────────────

    private int _hotToggleId = -1;
    private int _hotSearchId = -1;

    // ── UI ──────────────────────────────────────────────────────────────────

    private readonly Panel              _controlStrip;
    private readonly FlowLayoutPanel    _sectionsHost;
    private readonly SearchOverlay      _searchOverlay;
    private readonly List<SectionPanel> _sectionPanels = new();
    private          NotifyIcon?        _trayIcon;

    // Control-strip buttons (Segoe MDL2 Assets glyphs)
    private readonly Button _btnPin;       //  pin / always-on-top
    private readonly Button _btnSearch;    //  search
    private readonly Button _btnAdd;       //  add section
    private readonly Button _btnSettings;  //  settings
    private readonly Button _btnHide;      //  hide

    // ── Dock / resize constants ─────────────────────────────────────────────

    private const int DockSnapThreshold = 20;
    private const int ResizeBorder      = 6;

    private const int WM_NCHITTEST   = 0x0084;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private const int HTCLIENT      = 1;
    private const int HTLEFT        = 10, HTRIGHT       = 11;
    private const int HTTOP         = 12, HTBOTTOM      = 15;
    private const int HTTOPLEFT     = 13, HTTOPRIGHT    = 14;
    private const int HTBOTTOMLEFT  = 16, HTBOTTOMRIGHT = 17;

    private Size DefaultFloatingSize => new(
        Math.Max(240, _configService.Config.Window.IconSize * 6),
        Math.Max(120, _configService.Config.Window.IconSize * 3));

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainForm(ConfigService configService)
    {
        _configService = configService;
        _iconService   = new IconService();
        _launchService = new LaunchService(configService);

        // ── Window properties ─────────────────────────────────────────────

        FormBorderStyle = FormBorderStyle.None;
        TopMost         = configService.Config.Window.AlwaysOnTop;
        Opacity         = configService.Config.Window.Opacity;
        ShowInTaskbar   = true;
        DoubleBuffered  = true;
        BackColor       = SystemColors.Window;
        ForeColor       = SystemColors.WindowText;
        // Segoe UI is the Windows 11 system font; fall back gracefully if unavailable
        Font            = TryFont("Segoe UI", 9f) ?? SystemFonts.DefaultFont;
        Text            = "Personal TaskBar";
        MinimumSize     = new Size(200, 120);

        RestoreWindowGeometry();

        // ── Control strip ────────────────────────────────────────────────
        // Thin toolbar at the top; same background as the window so it blends in.
        // A single 1px hairline at the bottom separates it from the content area.

        _btnPin      = MakeStripButton("", "Toggle always-on-top");
        _btnSearch   = MakeStripButton("", "Search  (Ctrl+Space)");
        _btnAdd      = MakeStripButton("", "Add section");
        _btnSettings = MakeStripButton("", "Settings");
        _btnHide     = MakeStripButton("", "Hide  (tray icon to restore)");

        _controlStrip = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 30,
            BackColor = SystemColors.Window,
        };
        _controlStrip.Paint += PaintStripSeparator;

        var stripFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoSize      = false,
            BackColor     = Color.Transparent,
            Padding       = new Padding(1),
        };
        stripFlow.Controls.AddRange(new Control[]
            { _btnPin, _btnSearch, _btnAdd, _btnSettings, _btnHide });
        _controlStrip.Controls.Add(stripFlow);

        // ── Search overlay ────────────────────────────────────────────────

        _searchOverlay         = new SearchOverlay(_configService, _launchService);
        _searchOverlay.Visible = false;
        _searchOverlay.CloseRequested += (_, _) => CloseSearch();
        _searchOverlay.FilterChanged  += OnSearchFilter;

        // ── Sections host ─────────────────────────────────────────────────

        _sectionsHost = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoSize      = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            BackColor     = SystemColors.Window,
            Padding       = new Padding(4, 4, 4, 4),
        };

        // Add in reverse DockStyle priority: Fill first, then Top controls
        Controls.Add(_sectionsHost);
        Controls.Add(_searchOverlay);
        Controls.Add(_controlStrip);

        // ── Wire strip buttons ────────────────────────────────────────────

        _btnPin.Click      += (_, _) => ToggleAlwaysOnTop();
        _btnSettings.Click += (_, _) => OpenSettings();
        _btnHide.Click     += (_, _) => Hide();
        _btnSearch.Click   += (_, _) => OpenSearch();
        _btnAdd.Click      += (_, _) => AddSection();

        // ── Tray icon ─────────────────────────────────────────────────────
        // Always available so the user can show the bar or exit even when hidden.

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show / Hide", null, (_, _) => ToggleVisibility());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit",        null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon             = SystemIcons.Application,
            Text             = "Personal TaskBar",
            Visible          = true,
            ContextMenuStrip = trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();

        // ── Sections ──────────────────────────────────────────────────────
        // EnableDragOn is called in OnShown (after layout) so panels exist.

        RebuildSections();
        // Preload is deferred to OnShown so any exception is handled by Application.ThreadException
    }

    // ── DWM rounded corners + shadow ─────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDwmStyle();
        RegisterHotkeys();
    }

    private void ApplyDwmStyle()
    {
        try
        {
            // Rounded corners – Windows 11 only (silently ignored on Win10)
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            // Remove the DWM border so it doesn't fight with the window's own border
            int noBorder = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(Handle,
                NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        }
        catch { /* DWM not available on this OS version */ }
    }

    // Draw the hairline separator at the bottom of the control strip
    private void PaintStripSeparator(object? sender, PaintEventArgs e)
    {
        if (sender is not Control c) return;
        using var pen = new Pen(SystemColors.ControlLight, 1);
        e.Graphics.DrawLine(pen, 0, c.Height - 1, c.Width, c.Height - 1);
    }

    // ── WndProc (hotkeys + resize edges + single-instance) ───────────────────

    private const int HTCAPTION = 2; // needed for EnableDragOn

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && _configService != null)
        {
            // Decode screen coordinates; use signed shorts for multi-monitor support
            int lp     = m.LParam.ToInt32();
            var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var cursor = PointToClient(screen);

            bool left   = cursor.X <= ResizeBorder;
            bool right  = cursor.X >= ClientSize.Width  - ResizeBorder;
            bool top    = cursor.Y <= ResizeBorder;
            bool bottom = cursor.Y >= ClientSize.Height - ResizeBorder;

            var dock = _configService.Config.Window.Dock;

            // Don't offer a resize grip on edges that are glued to the screen
            if (dock == "top")    top    = false;
            if (dock == "bottom") bottom = false;
            if (dock == "left")   left   = false;
            if (dock == "right")  right  = false;

            int hit = HTCLIENT;
            if      (top    && left)   hit = HTTOPLEFT;
            else if (top    && right)  hit = HTTOPRIGHT;
            else if (bottom && left)   hit = HTBOTTOMLEFT;
            else if (bottom && right)  hit = HTBOTTOMRIGHT;
            else if (left)             hit = HTLEFT;
            else if (right)            hit = HTRIGHT;
            else if (top)              hit = HTTOP;
            else if (bottom)           hit = HTBOTTOM;

            if (hit != HTCLIENT) { m.Result = new IntPtr(hit); return; }
        }

        if (m.Msg == WM_EXITSIZEMOVE)
        {
            // Fires when the user finishes a native move or resize
            OnMoveOrResizeEnded();
        }

        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            _hotkeyService?.HandleHotkey(m.WParam.ToInt32());
        }
        else if (m.Msg == (int)NativeMethods.WM_ACTIVATE_INSTANCE)
        {
            if (!Visible) Show();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(Handle);
        }

        base.WndProc(ref m);
    }

    // ── Drag via ReleaseCapture + SendMessage ─────────────────────────────────

    /// <summary>
    /// Recursively subscribes MouseDown on every non-interactive container so
    /// clicking any empty area of the bar starts a native OS drag loop.
    /// </summary>
    private void EnableDragOn(Control c)
    {
        // Interactive controls need their own mouse handling; skip them.
        // EntryButton MUST be excluded — it handles its own Click for launching.
        if (c is Button or TextBox or RichTextBox or TrackBar
               or ComboBox or CheckBox or RadioButton or ScrollBar or EntryButton)
            return;

        c.MouseDown += OnSurfaceMouseDown;

        foreach (Control child in c.Controls)
            EnableDragOn(child);
    }

    private void OnSurfaceMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // When undocking from a full-edge dock, shrink back to a floating size
        // BEFORE the drag starts so the window isn't screen-width during the move.
        var dock = _configService.Config.Window.Dock;
        if (dock is "top" or "bottom")
        {
            Width = DefaultFloatingSize.Width;
            _configService.Config.Window.Dock = "none";
        }
        else if (dock is "left" or "right")
        {
            Height = DefaultFloatingSize.Height;
            _configService.Config.Window.Dock = "none";
        }

        // Hand the move loop to Windows. SendMessage blocks until mouse-up,
        // at which point WM_EXITSIZEMOVE fires and we snap + save.
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN,
            new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
    }

    // ── Post-move / resize snap ───────────────────────────────────────────────

    private void OnMoveOrResizeEnded()
    {
        TrySnap();
        SaveWindowGeometry();
    }

    // ── Hotkeys ──────────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService(Handle);

        var cfg = _configService.Config.Hotkeys;

        if (HotkeyService.TryParse(cfg.ToggleVisibility, out var mods, out var vk))
        {
            try { _hotToggleId = _hotkeyService.Register(mods, vk, ToggleVisibility); }
            catch { /* key already in use */ }
        }
        if (HotkeyService.TryParse(cfg.Search, out mods, out vk))
        {
            try { _hotSearchId = _hotkeyService.Register(mods, vk, OpenSearch); }
            catch { /* key already in use */ }
        }
    }

    // ── Window geometry ───────────────────────────────────────────────────────

    private void RestoreWindowGeometry()
    {
        var wc      = _configService.Config.Window;
        var screens = Screen.AllScreens;
        var screen  = wc.Monitor < screens.Length ? screens[wc.Monitor] : Screen.PrimaryScreen!;
        var wa      = screen.WorkingArea;

        // Clamp to a sensible floating size (docked positions override via ApplyDock)
        int safeW = wc.Dock is "top" or "bottom" ? wa.Width
                  : Math.Clamp(wc.Width,  200, wa.Width  / 2);
        int safeH = wc.Dock is "left" or "right"  ? wa.Height
                  : Math.Clamp(wc.Height,  120, wa.Height / 2);
        Size = new Size(safeW, safeH);

        int x = Math.Clamp(wc.X, wa.Left, Math.Max(wa.Left, wa.Right  - Width));
        int y = Math.Clamp(wc.Y, wa.Top,  Math.Max(wa.Top,  wa.Bottom - Height));
        Location = new Point(x, y);

        if (wc.Dock != "none")
            ApplyDock(wc.Dock, screen);
    }

    private void SaveWindowGeometry()
    {
        var wc    = _configService.Config.Window;
        wc.X      = Location.X;
        wc.Y      = Location.Y;
        wc.Width  = Width;
        wc.Height = Height;

        var screen  = Screen.FromControl(this);
        wc.Monitor  = Math.Max(0, Array.IndexOf(Screen.AllScreens, screen));
        _configService.SaveConfig();
    }

    // ── Dock ─────────────────────────────────────────────────────────────────

    private void TrySnap()
    {
        var screen = Screen.FromControl(this);
        var wa     = screen.WorkingArea;
        var loc    = Location;
        var sz     = Size;

        string dock = "none";
        if      (loc.Y - wa.Top                    <= DockSnapThreshold) dock = "top";
        else if (wa.Bottom - (loc.Y + sz.Height)   <= DockSnapThreshold) dock = "bottom";
        else if (loc.X - wa.Left                   <= DockSnapThreshold) dock = "left";
        else if (wa.Right  - (loc.X + sz.Width)    <= DockSnapThreshold) dock = "right";

        _configService.Config.Window.Dock = dock;
        if (dock != "none") ApplyDock(dock, screen);
    }

    private void ApplyDock(string dock, Screen screen)
    {
        var wa = screen.WorkingArea;
        switch (dock)
        {
            case "top":    Location = new Point(wa.Left, wa.Top);              Width  = wa.Width;  break;
            case "bottom": Location = new Point(wa.Left, wa.Bottom - Height);  Width  = wa.Width;  break;
            case "left":   Location = new Point(wa.Left, wa.Top);              Height = wa.Height; break;
            case "right":  Location = new Point(wa.Right - Width, wa.Top);     Height = wa.Height; break;
        }
    }

    // ── Resize override ───────────────────────────────────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_sectionsHost == null || _sectionPanels == null) return;

        int w = SectionWidth();
        foreach (var sp in _sectionPanels)
            sp.SetWidth(w);
        SaveWindowGeometry();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Force a width update now that the form is laid out and ClientSize is valid
        int w = SectionWidth();
        foreach (var sp in _sectionPanels)
            sp.SetWidth(w);
        // Wire drag to all controls (panels exist now; any exception is caught by ThreadException)
        EnableDragOn(this);
        // Pre-warm the icon cache (deferred from constructor so exceptions are catchable)
        _iconService.Preload(_configService.Sections, _configService.Config.Window.IconSize);
        Invalidate(true);
    }

    private int SectionWidth()
    {
        int w = _sectionsHost?.ClientSize.Width ?? 0;
        // Fall back to saved config width during construction (ClientSize may be 0)
        if (w < 10)
            w = Math.Max(80, _configService.Config.Window.Width
                             - (_sectionsHost?.Padding.Horizontal ?? 8));
        if (_sectionsHost?.VerticalScroll.Visible == true)
            w -= SystemInformation.VerticalScrollBarWidth;
        return Math.Max(80, w);
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    private void RebuildSections()
    {
        _sectionsHost.SuspendLayout();
        foreach (var sp in _sectionPanels) sp.Dispose();
        _sectionPanels.Clear();
        _sectionsHost.Controls.Clear();

        foreach (var sec in _configService.Sections)
        {
            var panel = CreateSectionPanel(sec);
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
        panel.EntryMovedToSection += (_, targetSection) =>
        {
            // Rebuild only the target panel so the moved entry appears there immediately
            var targetPanel = _sectionPanels.FirstOrDefault(p => p.SectionModel == targetSection);
            if (targetPanel != null)
            {
                targetPanel.RebuildEntries();
                EnableDragOn(targetPanel);
            }
        };
        panel.DataChanged += (_, _) => EnableDragOn(panel); // re-subscribe drag to new EntryButton panels

        return panel;
    }

    private void AddSection()
    {
        using var dlg = new Form
        {
            Text = "Add Section", Size = new Size(280, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            Font = Font,
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
        EnableDragOn(panel);         // subscribe drag to new panel's surfaces
        _sectionsHost.PerformLayout();
    }

    private void CollapseAllSections()
    {
        foreach (var sp in _sectionPanels) sp.SectionModel.Collapsed = true;
        _configService.SaveEntries();
        RebuildSections();
    }

    private void ExpandAllSections()
    {
        foreach (var sp in _sectionPanels) sp.SectionModel.Collapsed = false;
        _configService.SaveEntries();
        RebuildSections();
    }

    // ── Dialog helper (suspend TopMost so dialogs appear on top) ─────────────

    public DialogResult ShowDialogSafe(Form dlg)
    {
        bool was = TopMost;
        TopMost  = false;
        try     { return dlg.ShowDialog(this); }
        finally { TopMost = was; }
    }

    // ── Always-on-top ─────────────────────────────────────────────────────────

    private void ToggleAlwaysOnTop()
    {
        TopMost                              = !TopMost;
        _configService.Config.Window.AlwaysOnTop = TopMost;
        // Segoe MDL2: E718 = pin, E77A = unpin
        _btnPin.Text = TopMost ? "" : "";
        _configService.SaveConfig();
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    private void ToggleVisibility()
    {
        if (Visible) { Hide(); return; }
        Show();
        NativeMethods.SetForegroundWindow(Handle);
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_configService);
        dlg.IconSizeChanged    += (_, size)    => ApplyIconSize(size);
        dlg.AlwaysOnTopChanged += (_, top)    => { TopMost = top; };
        dlg.OpacityChanged     += (_, opacity) => { Opacity = opacity; };
        dlg.HotkeysChanged     += (_, _)      => RegisterHotkeys();
        ShowDialogSafe(dlg);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void OpenSearch()
    {
        if (!Visible) { Show(); NativeMethods.SetForegroundWindow(Handle); }
        _searchOverlay.Activate();
    }

    private void CloseSearch()
    {
        _searchOverlay.Deactivate();
        foreach (var sp in _sectionPanels) { sp.Visible = true; sp.RebuildEntries(); }
    }

    private void OnSearchFilter(object? sender, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var sp in _sectionPanels) sp.Visible = true;
            return;
        }
        foreach (var sp in _sectionPanels)
            sp.Visible = sp.SectionModel.Entries
                .Any(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    // ── Icon size ─────────────────────────────────────────────────────────────

    private void ApplyIconSize(int size)
    {
        _configService.Config.Window.IconSize = size;
        int w = SectionWidth();
        foreach (var sp in _sectionPanels) { sp.ApplyIconSize(size); sp.SetWidth(w); }
        _configService.SaveConfig();
    }

    // ── Exit ──────────────────────────────────────────────────────────────────

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        _hotkeyService?.Dispose();
        _iconService.Dispose();
        Application.Exit();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeStripButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Text      = glyph,
            Width     = 28,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = SystemColors.ControlText,
            // Segoe MDL2 Assets renders the icon glyphs
            Font      = TryFont("Segoe MDL2 Assets", 11f)
                        ?? new Font(SystemFonts.DefaultFont.FontFamily, 9f),
            Margin    = new Padding(1),
            Padding   = Padding.Empty,
            Cursor    = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize         = 0;
        btn.FlatAppearance.MouseOverBackColor  = SystemColors.ButtonHighlight;
        btn.FlatAppearance.MouseDownBackColor  = SystemColors.ButtonFace;
        new ToolTip().SetToolTip(btn, tooltip);
        return btn;
    }

    private static Font? TryFont(string name, float size)
    {
        try
        {
            var f = new Font(name, size);
            // If GDI substituted a different family, the font isn't available
            return f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? f : null;
        }
        catch { return null; }
    }
}
