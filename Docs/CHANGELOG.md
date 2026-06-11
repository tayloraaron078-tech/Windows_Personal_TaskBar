# Changelog

All notable changes to Personal TaskBar are documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)

---

## [1.0.0] – 2026-06-11

### Added

**Window Behaviour**
- Borderless, always-on-top floating toolbar for Windows 11
- Freely draggable by clicking any empty area of the bar
- Snap-to-dock: drags within 20 px of any screen edge (top/bottom/left/right) snap and stretch the bar to fill that edge
- Undock by dragging away from the edge
- User-resizable by dragging the non-docked edge
- Single `IconSize` value (24–96 px) drives all sizing: icon dimensions, label font, divider, padding, and spacing
- Multi-monitor support: remembers and restores the active monitor
- Position, dock state, monitor index, and icon size saved to `config.toml` on every move/resize

**Control Strip**
- Always-visible thin band at the top containing: always-on-top toggle (🔒/🔓), search (🔍), add section (+), settings (⚙), and hide (✕) buttons
- Buttons remain visible at minimum bar size

**Sections**
- Named groupings of entries with a visible label and coloured accent line
- Accent colour is user-configurable per section via colour picker
- Collapsible: click the header to fold/unfold with a simple animation
- Drag-to-reorder sections
- Section right-click context menu: Add Entry, Rename Section, Change Accent Color, Convert to Scratchpad, Collapse All, Expand All, Remove Section (with confirmation)
- Sections stored in `entries.toml`

**Entries**
- Four entry types: Executable (`.exe`), Folder, File (any type), URL
- Auto-extracted icons from executables via Shell API; system shell icons for folders and file types
- Custom icon override: any `.ico` or `.png` file
- User-defined name independent of filename
- Left-click to launch; `Process.Start` with `UseShellExecute = true` so UAC elevation prompts fire naturally
- Entry right-click menu: Edit Entry, Move to Section (submenu), Open File Location, Remove Entry (with confirmation)
- Drag-to-reorder within a section; drag-to-move between sections
- Recency indicator: green dot on entries launched within the last 7 days
- Last-launch timestamps stored as ISO 8601 strings in `entries.toml`
- Environment variable expansion (`%USERPROFILE%`, `%PROGRAMFILES%`, etc.) in all paths at launch time

**Display Modes (per section)**
- **Icons Only** – icon at full IconSize, no label; entry name in tooltip
- **Icons and Labels** – icon plus label below; both scale with IconSize
- **Labels Only** – compact text-only list; font scales with IconSize
- Mode stored in `entries.toml` per section

**Search**
- Global hotkey activation (default: `Ctrl+Space`) and search icon in control strip
- Live filtering of all entries across all sections by name
- Non-matching sections auto-collapse; matching sections auto-expand
- Press Enter to launch top result; press Escape to restore normal view

**Scratchpad**
- Any section can be set to type `scratchpad` via right-click menu
- Renders as a multi-line rich text area inside the bar
- Auto-saves content to `entries.toml` on every keystroke with a 500 ms debounce

**Settings Panel**
- IconSize slider (24–96 px) with live preview
- Toggle Hotkey and Search Hotkey configuration fields
- Launch at Windows startup toggle (writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
- Always-on-top toggle (mirrored from control strip)
- Open Config Folder button (Explorer)
- Open Quick Start Guide button (default markdown viewer or Notepad)
- Full Windows system theme compliance: `SystemColors` and `SystemFonts` throughout; no hardcoded colours or fonts

**Configuration**
- `config.toml` – window geometry, dock state, monitor, always-on-top, icon size, hotkeys, startup flag
- `entries.toml` – sections and entries including display mode, accent colour, collapse state, type, scratchpad content, and per-entry last-launch timestamps
- Both files stored next to the `.exe` (fully portable)
- Graceful first-run: missing files are created with sensible defaults and a sample "Getting Started" section containing a link to QUICKSTART.md
- Corrupt files reset to defaults automatically

**Single-Instance Enforcement**
- Named system Mutex prevents duplicate instances
- Second launch activates (brings to foreground) the existing window instead

**Icon Caching**
- All icons pre-loaded at startup into an in-memory cache keyed by (path, size)
- Repeated renders never re-hit the filesystem or shell

**Publish Profile**
- `dotnet publish` with no extra flags produces a single self-contained `.exe` targeting `win-x64`

**Documentation**
- `QUICKSTART.md` – 5-minute getting-started guide
- `MANUAL.md` – complete feature reference with sections for every feature area
- `CHANGELOG.md` – this file

### Changed
- N/A (initial release)

### Fixed
- N/A (initial release)
