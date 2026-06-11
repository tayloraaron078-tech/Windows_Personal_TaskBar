# Personal TaskBar – Full Feature Reference

---

## Table of Contents

1. [Window Behaviour](#1-window-behaviour)
2. [Sections](#2-sections)
3. [Entries](#3-entries)
4. [Display Modes](#4-display-modes)
5. [Search](#5-search)
6. [Scratchpad](#6-scratchpad)
7. [Settings](#7-settings)
8. [Config Files](#8-config-files)
9. [Keyboard Shortcuts](#9-keyboard-shortcuts)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. Window Behaviour

### Floating Toolbar
Personal TaskBar is a borderless, always-on-top window that floats above your other applications. It does not appear in the Windows Taskbar.

### Dragging
Click and drag any empty area of the bar (including the control strip at the top) to move it anywhere on your screen or across monitors. The position is saved automatically when you release the mouse.

### Docking
When you drag the bar within **20 pixels** of any screen edge, it snaps and stretches to fill that edge:

| Dock Position | Behaviour |
|--------------|-----------|
| **Top** | Bar stretches full-width along the top of the working area |
| **Bottom** | Bar stretches full-width along the bottom |
| **Left** | Bar stretches full-height along the left |
| **Right** | Bar stretches full-height along the right |

Drag the bar away from the edge to undock it.

### Resizing
Drag the non-docked edge of the bar to resize it. All content scales based on the single **Icon Size** value — see [Settings](#7-settings).

### Multi-Monitor Support
The bar remembers which monitor it was on and restores to that monitor on next launch. If the monitor is no longer connected, the bar falls back to the primary display.

### Control Strip
A thin band at the top of the bar always remains visible, even at minimum size. It contains:

| Button | Function |
|--------|----------|
| 🔒 | Toggle always-on-top (lock = pinned, 🔓 = unpinned) |
| 🔍 | Open search bar |
| **+** | Add a new section |
| ⚙ | Open settings |
| ✕ | Hide the bar (restore with the toggle hotkey) |

---

## 2. Sections

Sections are named groups of entries displayed inside the bar, separated by a coloured accent line.

### Creating a Section
Click the **+** button in the control strip, enter a name, and press Enter.

### Section Header
Each section displays its name in bold above a coloured accent line. Click the header to **collapse or expand** the section's contents. A ▶ or ▼ arrow indicates the collapsed/expanded state.

### Section Right-Click Menu

| Option | Description |
|--------|-------------|
| Display Mode | Switch between Icons Only, Icons and Labels, or Labels Only |
| Add Entry | Open the Add Entry dialog |
| Rename Section | Change the section name |
| Change Accent Color | Open the colour picker to change the accent line colour |
| Convert to Scratchpad | Replace entries with a plain-text scratchpad area |
| Collapse All | Fold every section |
| Expand All | Unfold every section |
| Remove Section | Delete this section and all its entries (with confirmation) |

### Reordering Sections
Drag a section header up or down to reorder sections. The new order is saved immediately.

---

## 3. Entries

Entries are the items you launch from the bar.

### Entry Types

| Type | Description |
|------|-------------|
| `exe` | Windows executable. Launched with `UseShellExecute = true` so UAC elevation works naturally. |
| `folder` | Opens in Windows Explorer |
| `file` | Opens with the registered application for that file type |
| `url` | Opens in the default browser |

### Icons
Icons are auto-extracted at startup and cached in memory:
- **Executables** – icon extracted from the .exe using Shell API
- **Folders** – Windows Explorer folder icon
- **Files** – icon from the registered file-type handler
- **URLs** – generic browser icon

You can override any entry's icon with a custom **.ico** or **.png** file via the Edit Entry dialog.

### Environment Variables in Paths
Paths in `entries.toml` may contain Windows environment variables such as `%USERPROFILE%`, `%PROGRAMFILES%`, `%APPDATA%`, etc. They are expanded at launch time.

### Recency Indicator
A small green dot appears on entries that were launched within the last **7 days**.

### Entry Right-Click Menu

| Option | Description |
|--------|-------------|
| Edit Entry | Change name, type, path, or icon override |
| Move to Section | Submenu of all other sections – moves this entry there |
| Open File Location | Opens Explorer at the entry's parent folder |
| Remove Entry | Removes this entry (with confirmation) |

### Reordering Entries
Drag an entry within its section to reorder it. Drag it onto a different section panel to move it there.

---

## 4. Display Modes

Each section has its own independently configured display mode.

| Mode | Description |
|------|-------------|
| **Icons Only** | Shows the icon at full Icon Size. Entry name appears in a tooltip on hover. |
| **Icons and Labels** | Shows the icon with the entry name as a label beneath it. Both scale with Icon Size. |
| **Labels Only** | Compact text-only list. Icons hidden. Font size scales with Icon Size. |

Change the display mode via the section's **right-click menu → Display Mode**.

The mode is stored per-section in `entries.toml`.

---

## 5. Search

### Activating Search
- Press the global hotkey (default: **Ctrl+Space**)
- Click the 🔍 button in the control strip

### How It Works
A text input appears at the top of the bar. As you type:
- Entries matching the query (by name, case-insensitive) remain visible.
- Sections with no matching entries are collapsed automatically.
- Sections with matches are expanded.

### Keyboard Actions in Search

| Key | Action |
|-----|--------|
| **Enter** | Launch the top matching entry |
| **Escape** | Close search and restore the normal view |

### Changing the Search Hotkey
Open ⚙ Settings and type a new combination in the **Search Hotkey** field (e.g. `Ctrl+F`).

---

## 6. Scratchpad

Any section can be converted to a **Scratchpad** via its right-click menu.

A scratchpad section renders as a small multi-line text area inside the bar. Use it for quick notes, clipboard snippets, or temporary text.

- Content is **auto-saved** to `entries.toml` on every keystroke with a 500 ms debounce — you never need to manually save.
- Font size scales with the global Icon Size setting.
- To convert back to a regular entry section, remove the section and add a new one.

---

## 7. Settings

Open with the **⚙** button in the control strip.

| Setting | Description |
|---------|-------------|
| **Icon Size** slider | Range 24–96 px. Changes take effect immediately (live preview). All spacing, label font size, and control dimensions scale from this single value. |
| **Opacity** slider | Range 15–100 %. Sets how transparent the bar body is. Icons are always fully visible regardless of this setting. Changes take effect immediately (live preview). Saved to `config.toml`. |
| **Toggle Hotkey** | Global hotkey to show/hide the bar. Default: `Ctrl+\`` |
| **Search Hotkey** | Global hotkey to open search. Default: `Ctrl+Space` |
| **Launch with Windows** | Writes/removes a registry key at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` to start the bar at login. |
| **Always on Top** | Mirrors the 🔒 button in the control strip. |
| **Open Config Folder** | Opens the folder containing `config.toml` and `entries.toml` in Explorer. |
| **Open Quick Start Guide** | Opens `QUICKSTART.md` in the default markdown viewer or Notepad. |

---

## 8. Config Files

Both files are stored **in the same folder as the .exe** (portable mode — no registry, no AppData).

### config.toml

```toml
[window]
x             = 100       # screen X position
y             = 100       # screen Y position
width         = 400
height        = 80
dock          = "none"    # none | top | bottom | left | right
monitor       = 0         # zero-based monitor index
always_on_top = true
icon_size     = 48        # 24–96
opacity       = 1.00      # 0.15–1.0 (15 %–100 %)

[hotkeys]
toggle_visibility = "Ctrl+`"
search            = "Ctrl+Space"

[startup]
launch_with_windows = false
```

### entries.toml

```toml
[[sections]]
name         = "Dev Tools"
accent_color = "#4A90D9"
display_mode = "icons_labels"   # icons_only | icons_labels | labels_only
collapsed    = false
type         = "entries"        # entries | scratchpad

  [[sections.entries]]
  name          = "VS Code"
  type          = "exe"         # exe | folder | file | url
  path          = "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe"
  icon_override = ""
  last_launched = "2026-06-10T14:23:00Z"

[[sections]]
name    = "Notes"
type    = "scratchpad"
content = """My quick notes go here."""
```

---

## 9. Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Ctrl+\`** (default) | Show / hide the bar |
| **Ctrl+Space** (default) | Open search |
| **Enter** (in search) | Launch top result |
| **Escape** (in search) | Close search |

Hotkeys are configurable in Settings. Any combination of `Ctrl`, `Alt`, `Shift`, `Win` plus a key is supported (e.g. `Ctrl+Alt+T`).

---

## 10. Troubleshooting

### The bar doesn't appear after launch
The bar may be hidden. Press the toggle hotkey (**Ctrl+\`** by default) to show it. If that doesn't work, delete `config.toml` so the bar resets to default position.

### A hotkey doesn't work
Another application may have registered the same key combination. Open Settings and try a different combination.

### An entry shows a generic icon instead of the app icon
- The path may be incorrect or the file may have been moved.
- Verify the path in **Edit Entry**.
- Alternatively, set an **Icon Override** pointing to a `.ico` or `.png` file.

### The bar disappeared from my second monitor
If the monitor is disconnected the bar falls back to the primary display. Re-attach the monitor, then drag the bar back to it.

### Config files are corrupt
Delete `config.toml` or `entries.toml` (or both). On next launch the app recreates them with defaults.

### The app won't start (second instance)
If the process is still running in the background (visible in Task Manager), terminate it and relaunch.

### UAC prompt doesn't appear for elevated apps
Make sure the entry type is set to `exe` and that `UseShellExecute = true` is in effect (this is the default). Running Personal TaskBar itself as Administrator suppresses UAC prompts for child processes — run it as a normal user.
