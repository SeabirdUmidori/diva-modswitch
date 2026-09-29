# Changelog

All notable changes to this tool are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses semantic versioning.

## [1.0.0] - 2026-09-30

First public release.

### Added

- **Enable / disable a mod** by clicking the ☑/☐ glyph at the head of a row, or Space on the selected
  row. Writes exactly the `enabled` line of that mod's own `config.toml`, then verifies that no other
  line in the file moved.
- **Priority reordering** by dragging a row, with a live insertion line showing where it will land;
  also ↑/↓ buttons and Alt+↑/↓. Writes only the `priority` line of the loader's root `config.toml`.
- **Multi-select** with Ctrl/Shift-click, and **batch delete** behind one confirmation dialog. Deleting
  moves the folder to `modswitch-deleted\<timestamp>\` instead of erasing it, then re-derives
  `priority`.
- **Install a mod pack** from a dropped or picked archive: `.zip` read in process, `.7z` and `.rar`
  decoded by the `System32\tar.exe` that ships with Windows, so the tool stays one file with nothing to
  install. The container is chosen by the archive's own header, and every path is checked that it stays
  inside the mod folder it was meant for.
- **Install / remove the loader component** through one button whose label reflects the current state,
  and a **debug console** checkbox wired to the loader's `console` setting.
- **Check**: a read-only replay of the loader's search rules (`<mod>\<include entry>\rom`), reporting
  packs that would register nothing, `rom/` folders missing from `include`, declared `dll` files that
  are absent, and enabled mods that are not in `priority`.
- **Undo** of the last toggle or reorder, from `modswitch-backup\`.
- **Game folder detection**: remembered choice, then a drive scan, then the Steam registry with
  `libraryfolders.vdf` and the app manifest; also editable or browsable by hand.
- **`modswitch-cli.exe`**: the same engine with a command-line entry point (`ls`, `audit`, `on/off/toggle`,
  `up/down/move`, `console`, `install-loader`, `remove-loader`, `undo`, `game`, `--dry`). `--dry` is scoped to its own command; it used to leave a
  process-wide flag behind, so a later call in the same process refused to write while still reporting
  success.
- **DivaModLoader's `dinput8.dll` compiled into the exe** as the last-resort payload: unmodified,
  MIT, hashes recorded in `THIRD_PARTY.md`. Precedence stays with the user - `loader\dinput8.dll` first,
  then a copy already in the game folder (never overwritten), then the bundled one.
- **Three languages** (中文 / English / 日本語) defaulting to the OS UI language, switchable in place.
- **Headless acceptance run** (`build.bat test`) that builds a fake game folder under `_selftest\` and
  drives the real install / delete / toggle / reorder / layout code, 71 assertions.

### Known limitations

- Encrypted `.7z` / `.rar` archives cannot be opened (the system decoder has no key); the failure is
  reported with its own message.
- `.rar` reading depends on the `tar.exe` shipped with Windows 10 1803 or later.
- Loader components and the debug console are deliberately not undoable.

[1.0.0]: https://github.com/SeabirdUmidori/diva-modswitch/releases/tag/v1.0.0
