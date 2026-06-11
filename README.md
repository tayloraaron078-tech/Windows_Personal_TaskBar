# Personal TaskBar

A fully customisable Windows launcher bar for power users — dock it to any screen edge, fill it with shortcuts, and launch anything in one click.

---

## Features

- **Dockable floating toolbar** — snap to top, bottom, left, or right of any monitor, or leave it floating
- **Sections** — named groups with coloured accent lines, collapsible, drag-to-reorder
- **Entries** — launch executables, folders, files, or URLs; icons auto-extracted from targets
- **Three display modes** per section: Icons Only, Icons + Labels, Labels Only
- **Live search** — `Ctrl+Space` to filter all entries instantly; Enter to launch top result
- **Scratchpad sections** — in-bar text area that auto-saves as you type
- **Single `IconSize` slider** (24–96 px) scales everything proportionally
- **Adjustable opacity** — set the bar transparency from 15 % to fully opaque; icons always remain visible
- **Portable** — all config stored in `config.toml` and `entries.toml` next to the `.exe`; no installer, no AppData
- **Windows theme aware** — uses `SystemColors`/`SystemFonts` throughout; works with light and dark mode automatically

---

## Getting Started

See [Docs/QUICKSTART.md](Docs/QUICKSTART.md) to be up and running in under 5 minutes.

For a full feature reference see [Docs/MANUAL.md](Docs/MANUAL.md).

---

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Restore dependencies and build a single self-contained exe
dotnet publish
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Personal_TaskBar.exe`

---

## Project Structure

```
Personal_TaskBar.csproj   — project file; publish profile included
Program.cs                — entry point, single-instance enforcement
NativeMethods.cs          — Win32 P/Invoke (hotkeys, icon extraction, etc.)
MainForm.cs               — main window: drag, dock, resize, control strip
Models/
  AppConfig.cs            — config.toml data model
  Section.cs              — section data model
  Entry.cs                — entry data model
Services/
  ConfigService.cs        — load/save TOML config files
  LaunchService.cs        — Process.Start, UAC, last-launched tracking
  IconService.cs          — icon extraction with in-memory cache
  HotkeyService.cs        — global hotkey registration via Win32
UI/
  SectionPanel.cs         — section header, accent line, collapse, reorder
  EntryButton.cs          — per-entry control (icon, label, recency dot)
  SearchOverlay.cs        — live-filter search bar
  ScratchpadPanel.cs      — auto-saving text area
  SettingsForm.cs         — settings dialog
  EditEntryForm.cs        — add/edit entry dialog
Docs/
  QUICKSTART.md
  MANUAL.md
  CHANGELOG.md
```

---

## License

See [license](license).
