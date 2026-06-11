# Changelog

All notable changes to Personal TaskBar are documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)

---

## [1.1.0] – 2026-06-11

### Added
- **Adjustable window opacity** — a new Opacity slider (15–100 %) in Settings lets you set how transparent the bar body appears. Icons are always rendered fully opaque regardless of the opacity value. The setting is persisted as `opacity` in `config.toml` and applied immediately as a live preview while dragging the slider.

### Fixed
- **Startup crash (silent exit before window appears)** — `SHGetImageList` / `IImageList` COM vtable mismatch caused an unrecoverable `AccessViolationException` in .NET 8, killing the process before the message loop started. Removed COM interop entirely; icon extraction now uses `Icon.ExtractAssociatedIcon` (managed) and `SHGetFileInfo` (safe P/Invoke only).
- **Entries not launching** — `Click` events on `EntryButton` were being absorbed by the parent `FlowLayoutPanel`'s `MouseDown` drag subscription. Switched to `MouseUp + _hovered` tracking and excluded `EntryButton` from `EnableDragOn`. Added `Margin = Padding.Empty` so no gap pixels steal mouse events.
- **Icons invisible** — `Icon.ToBitmap()` and `g.Clear(Color.Transparent)` both produce bitmaps with alpha = 0 on Windows masked icons. Fixed by using `DrawIconEx` (via P/Invoke) for all HICON→Bitmap conversions, and `g.Clear(BackColor)` in DoubleBuffered paint.
- **Move to Section target panel not updated** — Added `EntryMovedToSection` event on `SectionPanel`; `MainForm` now calls `RebuildEntries()` on the target panel immediately after a move.
- **Icon not refreshing after Edit Entry without restart** — Changed `btn.Invalidate()` to `btn.ApplyIconSize()` after an edit to bust the icon cache.
- **Path not found with quoted paths** — Added `Trim('"')` at every path boundary (load, launch, icon service, edit form) to strip surrounding quotes that Windows Explorer sometimes adds.
- **Dialogs hidden behind TopMost window** — `ShowDialogSafe()` now temporarily drops `TopMost` before opening any dialog.
- **Entries still visible in wrong section after section rebuild** — `_content.Location` is now always set explicitly to avoid `FlowLayoutPanel` placing child controls over the section header.
- **`AccessViolationException` from double `EnableDragOn` call** — Removed `EnableDragOn` from the constructor; it is now called only in `OnShown` after layout.
- **Crash exceptions silently discarded before message loop** — Added `AppDomain.CurrentDomain.UnhandledException` handler in `Program.cs` to surface pre-loop crashes with a visible message box.

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
