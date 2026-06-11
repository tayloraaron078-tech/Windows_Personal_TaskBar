using System;
using System.IO;
using System.Windows.Forms;
using Personal_TaskBar.Models;

namespace Personal_TaskBar.UI;

/// <summary>
/// Dialog for adding a new entry or editing an existing one.
/// Edits the Entry object in place; caller persists to disk on OK.
/// </summary>
public class EditEntryForm : Form
{
    private readonly Entry _entry;
    private readonly bool  _isNew;

    // Controls
    private readonly TextBox   _tbName;
    private readonly ComboBox  _cbType;
    private readonly TextBox   _tbPath;
    private readonly TextBox   _tbIconOverride;
    private readonly Button    _btnBrowsePath;
    private readonly Button    _btnBrowseIcon;
    private readonly Button    _btnOk;
    private readonly Button    _btnCancel;

    public EditEntryForm(Entry entry, bool isNew)
    {
        _entry = entry;
        _isNew = isNew;

        Text            = isNew ? "Add Entry" : "Edit Entry";
        Size            = new System.Drawing.Size(420, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = System.Drawing.SystemColors.Control;
        ForeColor       = System.Drawing.SystemColors.ControlText;
        Font            = System.Drawing.SystemFonts.DefaultFont;

        // ── Build layout ─────────────────────────────────────────────────

        var table = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 5,
            Padding     = new Padding(8),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        _tbName         = new TextBox { Text = entry.Name,         Dock = DockStyle.Fill };
        _cbType         = new ComboBox{ Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _tbPath         = new TextBox { Text = entry.Path,         Dock = DockStyle.Fill };
        _tbIconOverride = new TextBox { Text = entry.IconOverride,  Dock = DockStyle.Fill };
        _btnBrowsePath  = new Button  { Text = "Browse…",          Dock = DockStyle.Fill };
        _btnBrowseIcon  = new Button  { Text = "Browse…",          Dock = DockStyle.Fill };
        _btnOk          = new Button  { Text = "OK",     DialogResult = DialogResult.OK };
        _btnCancel      = new Button  { Text = "Cancel", DialogResult = DialogResult.Cancel };

        _cbType.Items.AddRange(new object[] { "exe", "folder", "file", "url" });
        _cbType.SelectedItem = entry.Type;
        if (_cbType.SelectedIndex < 0) _cbType.SelectedIndex = 0;

        // Row 0 – Name
        table.Controls.Add(new Label { Text = "Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        table.Controls.Add(_tbName,   1, 0);
        table.SetColumnSpan(_tbName,  2);

        // Row 1 – Type
        table.Controls.Add(new Label { Text = "Type:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        table.Controls.Add(_cbType,   1, 1);
        table.SetColumnSpan(_cbType,  2);

        // Row 2 – Path
        table.Controls.Add(new Label { Text = "Path / URL:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        table.Controls.Add(_tbPath,         1, 2);
        table.Controls.Add(_btnBrowsePath,  2, 2);

        // Row 3 – Icon override
        table.Controls.Add(new Label { Text = "Icon Override:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        table.Controls.Add(_tbIconOverride, 1, 3);
        table.Controls.Add(_btnBrowseIcon,  2, 3);

        // Row 4 – Buttons
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        btnPanel.Controls.AddRange(new Control[] { _btnCancel, _btnOk });
        table.Controls.Add(btnPanel, 0, 4);
        table.SetColumnSpan(btnPanel, 3);

        Controls.Add(table);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        // ── Events ──────────────────────────────────────────────────────

        _btnBrowsePath.Click  += OnBrowsePath;
        _btnBrowseIcon.Click  += OnBrowseIcon;
        _btnOk.Click          += OnOkClicked;
    }

    // ── Browse dialogs ──────────────────────────────────────────────────────

    private void OnBrowsePath(object? sender, EventArgs e)
    {
        var type = _cbType.SelectedItem?.ToString() ?? "exe";

        switch (type)
        {
            case "exe":
                using (var ofd = new OpenFileDialog { Filter = "Executables|*.exe|All Files|*.*", Title = "Select executable" })
                    if (ofd.ShowDialog() == DialogResult.OK)
                        _tbPath.Text = ofd.FileName;
                break;

            case "folder":
                using (var fbd = new FolderBrowserDialog { Description = "Select folder" })
                    if (fbd.ShowDialog() == DialogResult.OK)
                        _tbPath.Text = fbd.SelectedPath;
                break;

            case "file":
                using (var ofd = new OpenFileDialog { Filter = "All Files|*.*", Title = "Select file" })
                    if (ofd.ShowDialog() == DialogResult.OK)
                        _tbPath.Text = ofd.FileName;
                break;

            default: // url — no browser dialog; user types it
                break;
        }
    }

    private void OnBrowseIcon(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Icon/Image files|*.ico;*.png;*.bmp;*.jpg|All Files|*.*",
            Title  = "Select icon image",
        };
        if (ofd.ShowDialog() == DialogResult.OK)
            _tbIconOverride.Text = ofd.FileName;
    }

    // ── Validation and commit ───────────────────────────────────────────────

    private void OnOkClicked(object? sender, EventArgs e)
    {
        var name = _tbName.Text.Trim();
        var path = _tbPath.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Name is required.", "Personal TaskBar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Path or URL is required.", "Personal TaskBar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        // Commit changes to the model
        _entry.Name         = name;
        _entry.Type         = _cbType.SelectedItem?.ToString() ?? "exe";
        _entry.Path         = path;
        _entry.IconOverride = _tbIconOverride.Text.Trim();
    }
}
