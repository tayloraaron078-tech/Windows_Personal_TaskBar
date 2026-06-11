using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Personal_TaskBar.Models;
using Personal_TaskBar.Services;

namespace Personal_TaskBar.UI;

/// <summary>
/// Settings panel shown as a child form docked inside the main window.
/// Uses SystemColors/SystemFonts throughout so it matches the Windows theme automatically.
/// </summary>
public class SettingsForm : Form
{
    private readonly ConfigService _configService;
    private readonly AppConfig     _config;

    // Controls
    private readonly TrackBar   _sldIconSize;
    private readonly Label      _lblIconSizeValue;
    private readonly TrackBar   _sldOpacity;
    private readonly Label      _lblOpacityValue;
    private readonly TextBox    _tbToggleHotkey;
    private readonly TextBox    _tbSearchHotkey;
    private readonly CheckBox   _chkStartup;
    private readonly CheckBox   _chkAlwaysOnTop;
    private readonly Button     _btnOpenConfigFolder;
    private readonly Button     _btnOpenQuickstart;
    private readonly Button     _btnClose;

    /// <summary>Raised when the user changes the icon size so the main form can live-preview.</summary>
    public event EventHandler<int>? IconSizeChanged;

    /// <summary>Raised when the user changes the always-on-top toggle.</summary>
    public event EventHandler<bool>? AlwaysOnTopChanged;

    /// <summary>Raised when the user changes the opacity slider so the main form can live-preview.</summary>
    public event EventHandler<double>? OpacityChanged;

    /// <summary>Raised when hotkey strings change so the main form re-registers them.</summary>
    public event EventHandler? HotkeysChanged;

    public SettingsForm(ConfigService configService)
    {
        _configService = configService;
        _config        = configService.Config;

        Text            = "Personal TaskBar – Settings";
        Size            = new System.Drawing.Size(360, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = System.Drawing.SystemColors.Control;
        ForeColor       = System.Drawing.SystemColors.ControlText;
        Font            = System.Drawing.SystemFonts.DefaultFont;

        // ── Layout ──────────────────────────────────────────────────────────

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            Padding     = new Padding(10),
            AutoSize    = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        // Icon size slider
        _sldIconSize      = new TrackBar { Minimum = 24, Maximum = 96, Value = _config.Window.IconSize,
                                           TickFrequency = 8, Dock = DockStyle.Fill };
        _lblIconSizeValue = new Label    { Text = _config.Window.IconSize + "px",
                                           TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                                           Dock = DockStyle.Fill };

        AddRow(layout, "Icon Size:", _sldIconSize);
        AddRow(layout, string.Empty, _lblIconSizeValue);

        // Opacity slider (15–100 maps to 0.15–1.0)
        int opacityPct = (int)Math.Round(_config.Window.Opacity * 100);
        _sldOpacity      = new TrackBar { Minimum = 15, Maximum = 100, Value = opacityPct,
                                          TickFrequency = 5, Dock = DockStyle.Fill };
        _lblOpacityValue = new Label    { Text = opacityPct + "%",
                                          TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                                          Dock = DockStyle.Fill };
        AddRow(layout, "Opacity:", _sldOpacity);
        AddRow(layout, string.Empty, _lblOpacityValue);

        // Toggle hotkey
        _tbToggleHotkey = new TextBox { Text = _config.Hotkeys.ToggleVisibility, Dock = DockStyle.Fill };
        AddRow(layout, "Toggle Hotkey:", _tbToggleHotkey);

        // Search hotkey
        _tbSearchHotkey = new TextBox { Text = _config.Hotkeys.Search, Dock = DockStyle.Fill };
        AddRow(layout, "Search Hotkey:", _tbSearchHotkey);

        // Startup
        _chkStartup = new CheckBox { Text = "Launch with Windows",
                                      Checked = _config.Startup.LaunchWithWindows, AutoSize = true };
        AddRowSpan(layout, _chkStartup);

        // Always on top
        _chkAlwaysOnTop = new CheckBox { Text = "Always on Top",
                                          Checked = _config.Window.AlwaysOnTop, AutoSize = true };
        AddRowSpan(layout, _chkAlwaysOnTop);

        // Config folder button
        _btnOpenConfigFolder = new Button { Text = "Open Config Folder", Dock = DockStyle.Fill };
        AddRowSpan(layout, _btnOpenConfigFolder);

        // Quickstart button
        _btnOpenQuickstart = new Button { Text = "Open Quick Start Guide", Dock = DockStyle.Fill };
        AddRowSpan(layout, _btnOpenQuickstart);

        // Close button
        _btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Fill };
        AddRowSpan(layout, _btnClose);
        AcceptButton = _btnClose;

        Controls.Add(layout);

        // ── Events ──────────────────────────────────────────────────────────

        _sldIconSize.ValueChanged += (_, _) =>
        {
            // Snap to multiples of 8 for clean scaling
            int snapped = ((_sldIconSize.Value + 4) / 8) * 8;
            snapped     = Math.Clamp(snapped, 24, 96);

            _lblIconSizeValue.Text    = snapped + "px";
            _config.Window.IconSize   = snapped;
            IconSizeChanged?.Invoke(this, snapped);
        };

        _sldOpacity.ValueChanged += (_, _) =>
        {
            int pct = _sldOpacity.Value;
            _lblOpacityValue.Text    = pct + "%";
            _config.Window.Opacity   = pct / 100.0;
            OpacityChanged?.Invoke(this, _config.Window.Opacity);
        };

        _chkAlwaysOnTop.CheckedChanged += (_, _) =>
        {
            _config.Window.AlwaysOnTop = _chkAlwaysOnTop.Checked;
            AlwaysOnTopChanged?.Invoke(this, _chkAlwaysOnTop.Checked);
        };

        _chkStartup.CheckedChanged += (_, _) =>
        {
            _config.Startup.LaunchWithWindows = _chkStartup.Checked;
            ApplyStartupRegistry(_chkStartup.Checked);
        };

        _btnOpenConfigFolder.Click += (_, _) =>
            Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(ConfigService.ConfigPath)}\"");

        _btnOpenQuickstart.Click += (_, _) => OpenQuickstart();

        FormClosing += (_, _) =>
        {
            _config.Hotkeys.ToggleVisibility = _tbToggleHotkey.Text.Trim();
            _config.Hotkeys.Search           = _tbSearchHotkey.Text.Trim();
            _configService.SaveConfig();
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    // ── Startup registry ────────────────────────────────────────────────────

    private static void ApplyStartupRegistry(bool enable)
    {
        const string regKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "Personal_TaskBar";

        using var key = Registry.CurrentUser.OpenSubKey(regKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            // Environment.ProcessPath is the actual .exe path even in single-file publish
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName, throwOnMissingValue: false);
        }
    }

    // ── Quickstart opener ───────────────────────────────────────────────────

    private static void OpenQuickstart()
    {
        var path = Path.Combine(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            "Docs", "QUICKSTART.md");

        if (!File.Exists(path))
        {
            MessageBox.Show("QUICKSTART.md not found at:\n" + path,
                "Personal TaskBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Try shell open (uses default .md viewer), fall back to Notepad
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", $"\"{path}\""); }
    }

    // ── Layout helpers ──────────────────────────────────────────────────────

    private static void AddRow(TableLayoutPanel t, string label, Control ctrl)
    {
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label,
                                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                                    Dock = DockStyle.Fill });
        t.Controls.Add(ctrl);
    }

    private static void AddRowSpan(TableLayoutPanel t, Control ctrl)
    {
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int row = t.RowCount++;
        t.Controls.Add(ctrl, 0, row);
        t.SetColumnSpan(ctrl, 2);
    }
}
