# diva-modswitch

A small mod enabler for **Hatsune Miku Project DIVA Mega Mix+** on PC: it turns mods on and off,
reorders them, installs mod packs from an archive, and checks whether the loader will actually find
what you have on disk. One `.exe`, no installer, no runtime to download, works in
**中文 / English / 日本語**.

It exists because the tool everyone used before this one could silently break an install.

## Get it

- **Binary**: grab `modswitch-gui.exe` from this repository's **Releases** page. It is one file -
  put it in any folder you like (outside the game directory is fine); it keeps its own state next to
  itself.
  No installer, nothing else to download. Because it is unsigned, SmartScreen may warn once; the source
  is here, so you can also build it yourself.
- **From source**: run `build.bat`. Only the .NET Framework compiler that is already part of Windows is
  used - no SDK, no NuGet restore. See [Build](#build).

---

## Why

DivaModManager 1.3.1.0 ([TekkaGB/DivaModManager](https://github.com/TekkaGB/DivaModManager)) rewrites a
mod's `include` array whenever you click it:

```csharp
config["include"] = new string[1] { "." };
```

That line runs unconditionally on both the enable and the disable path, even when the file parsed
correctly. A pack that ships *"one folder + one include directory per module"* - which is how several
big module packs are built, one of them with **39** entries - therefore stops loading entirely the
moment you click it. Nothing warns you: DivaModLoader builds each search root as
`<mod folder>\<include entry>\rom` and quietly drops the roots that do not exist, so a wrong
`include` list registers no databases and the mods simply vanish.

**This tool never touches `include`.** It edits exactly one line per file and proves afterwards that
no other line moved.

## What it guarantees

| | |
|---|---|
| **One line per write** | `enabled` in a mod's `config.toml`, `priority` or `console` in the loader's root `config.toml`. |
| **Self-verified** | after writing, every other line is compared byte for byte; if more than the one intended line changed, the write is refused and rolled back. |
| **Byte-exact** | files are read and written as Latin-1 (1 char = 1 byte), so CRLF, BOM state, comments and a long `include` array survive untouched. Author names are decoded as UTF-8 only for display. |
| **Nothing in your game folder that you did not ask for** | its own files (order sidecar, backups, trash, loader payload) live next to the `.exe`. The only things it puts in the game folder are the loader component and root config you asked it to install. |
| **Deletion is a rename** | a deleted - or replaced - mod moves to `modswitch-deleted\<timestamp>\<name>` on the same drive. Nothing is erased. |
| **Undo** | the last toggle or reorder can be reverted; originals are kept under `modswitch-backup\`. Loader components and the debug console are deliberately not undoable (they are restored by pressing *Install* again). |
| **Offline** | no network code at all. It only reads the local Steam registry/`.vdf` to find the game folder. |

## Features

- **Toggle a mod**: click the ☑/☐ at the head of a row, or press Space on the selected row.
- **Reorder priority**: drag a row, or Alt+↑/↓, or the ↑/↓ buttons. A live line shows where it will land.
- **Multi-select**: Ctrl/Shift-click rows, then delete several packs in one confirmation.
- **Install a mod pack**: drop a `.zip`, `.7z` or `.rar` anywhere on the window (or pick one).
- **Remove / install the loader component**, and a **debug console** checkbox that writes the loader's
  own `console` setting. DivaModLoader's `dinput8.dll` is compiled in as the last-resort payload, so a
  machine that has mods but no loader needs no download (hashes and licence: THIRD_PARTY.md).
- **Check** replays the loader's algorithm over your install, read-only, and tells you which mods
  will register nothing, which `rom/` folders are missing from `include`, which declared `dll` files
  are absent, and which enabled mods are not in `priority`.
- **Game folder**: found automatically (drive scan + Steam registry + `libraryfolders.vdf`), or typed
  in / browsed and then remembered.
- **Three languages**, defaulting to your OS language, switchable in place without restarting.

## Requirements

- Windows 10 (1803 or later) or Windows 11. `.NET Framework 4.x` is already part of both - that is the
  only runtime needed, and the compiled binary runs without any installation step.
- `.7z` and `.rar` are decoded by **`System32\tar.exe`** (bsdtar / libarchive), which ships with those
  Windows versions, so no third-party decompressor is bundled. Consequence: **encrypted archives
  cannot be opened** - the tool shows `tar`'s own message instead of guessing.
- **DivaModLoader** is a separate project by someone else ([blueskythlikesclouds/DivaModLoader](https://github.com/blueskythlikesclouds/DivaModLoader),
  MIT). One unmodified copy of its `dinput8.dll` is **compiled into this exe** so a machine that has mods
  but no loader can be made to load them in one click, with nothing to download. Precedence is always
  yours: a `loader\dinput8.dll` you put next to the exe wins, then a `dinput8.dll` already in the game
  folder (which gets kept, never overwritten), and only then the bundled copy. Which exact bytes we ship
  - size, MD5, SHA-256, and the licence text - is recorded in [THIRD_PARTY.md](THIRD_PARTY.md); that
  project's own Releases page stays authoritative. Building without the bundled loader is one line in
  `build.bat`.

## Build

```
build.bat          ->  dist\modswitch-gui.exe    the window
                        dist\modswitch-cli.exe   the same engine without a window
build.bat test     ->  dist\modswitch-test.exe   headless acceptance run (exit 0 = everything passed)
```

`build.bat` only calls `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. No SDK, no Visual
Studio, no NuGet, no package restore. The sources are C# 5 (that is what the in-box compiler
supports) and must keep their UTF-8 BOM so the Chinese comments survive.

### The self-test

`src/modswitch-test.cs` is not part of the shipped exe. It builds a **fake game folder** under
`_selftest\` next to the test binary, then drives the real install / delete / toggle / reorder /
layout code - and the console entry point too, called in process - printing one `ok` / `BAD` line per
assertion (currently 71). It answers its own modal confirmation dialogs, and because the test binary
lives in its own folder, the order sidecar, backups and trash it writes land beside *it* - never inside
a real install.

Point `MODSWITCH_SAMPLE` at a real archive to also exercise that container end to end (the test writes
its own order sidecar, backups and trash next to the test binary, so run it from `dist\`):

```
set MODSWITCH_SAMPLE=C:\Users\you\Downloads\some_mod.7z
dist\modswitch-test.exe
```

## Command line

The same engine also builds as `modswitch-cli.exe` - handy in a script, over a remote session, or when
you would rather not open a window:

```
modswitch-cli --game "<game folder>" ls
modswitch-cli audit
modswitch-cli on|off|toggle <mod>
modswitch-cli up|down <mod>
modswitch-cli move <mod> <row number, 1 = highest priority>
modswitch-cli console on|off
modswitch-cli install-loader | remove-loader
modswitch-cli undo
modswitch-cli game "<folder>"
modswitch-cli --dry <anything>
```

There is no second implementation: every command reaches the same verified one-line writer as the
window, and `--dry` prints what would change without touching a file. Note that `priority` only lists
*enabled* mods, so reordering a mod you just switched off is a no-op until you switch it back on.

## Using it

1. Run `modswitch-gui.exe`. It looks for the game; if it guesses wrong, type the folder or use
   *Choose game folder* - the only requirement is that `DivaMegaMix.exe` is in it.
2. Press **Check** first. Red rows are mods that will not load, orange rows are enabled but missing
   from `priority`.
3. Click a row's ☑/☐ to switch a mod, drag rows to order them.
4. Drop an archive on the window to install a pack. Restart the game to apply anything.

### How an archive becomes a mod

A **package** is the folder inside the archive that holds `config.toml`. The shallowest such folder
wins, so a wrapper directory an upload site put around the real folder is dropped rather than copied
in. Its contents land in `mods\<name>\`, where `<name>` is the `name` key from that `config.toml`
(the archive's file name is the fallback, and invalid path characters are stripped). If the target
already exists you are asked first, and the old copy goes to the trash folder before the new one is
written. Every extracted path is checked that it stays inside that mod folder; entries that try to
climb out (`..\..\something`) are skipped and reported, and the rest of the package still installs.

The container is decided **by the file's own header**, not by its extension, because renamed packs are
common: `PK\x03\x04` → zip (read in-process), `7z\xBC\xAF\x27\x1C` → 7z, `Rar!\x1A\x07` → rar (v3 or v5).

## Notes for anyone editing the UI

Several things here are the way they are because a native WinForms behaviour bit them:

- **No native checkbox column.** A `ListView` with `CheckBoxes = true` replays `ItemChecked` in
  False/True pairs whenever the selection churns - including for a click in the middle of the row text,
  far from the box. Treating that event as "the user switched this mod" writes config files on a race.
  The state is a glyph in column 0 instead, and only an explicit click on it (or Space) toggles.
- **A press is not a drag.** `DoDragDrop` called from `MouseDown` runs the OLE loop for every click and
  swallows the `WM_LBUTTONDOWN` the list-view needs for Ctrl/Shift selection. A press only records the
  row and the modifiers it saw; `MouseMove` starts the drag once the pointer leaves the system drag box.
- **A toggle does not rebuild the table.** `Items.Clear()` destroys the row objects and the
  multi-selection with them, so rows are re-celled in place.
- **Strips are measured, not AutoSize.** `AutoSize` asks a `FlowLayoutPanel` for its preferred size with
  no width constraint, so it always answers "one row" and the overflow buttons fall outside the strip.
  Both the button strip and the hint bar are measured at the width they actually have.
- **`ZipArchiveEntry.FullName` uses `\` on Windows**, not the `/` the archive stores. Match and cut on
  the normalised form, or a wrapped package silently installs as an empty folder.
- `Loc.F` with an unknown key returns the key instead of throwing, which is why the self-test also walks
  the whole string table and checks that all three languages use the same `{n}` placeholders.

## Layout

```
diva-modswitch/
  build.bat                 in-box csc, no dependencies
  src/modswitch.cs          the engine: finding the game, byte-exact one-line edits, audit, undo
  src/modswitch-gui.cs      the window: table, drag reorder, install/delete, checks
  src/modswitch-loc.cs      the 中文 / English / 日本語 string table
  src/modswitch-test.cs     the headless acceptance run (not part of the exe)
  payload/dinput8.dll       DivaModLoader, unmodified, compiled in (THIRD_PARTY.md)
  THIRD_PARTY.md            which bytes are bundled, their hashes, and the licence
  THIRD_PARTY/DivaModLoader/LICENSE.md
  dist/                     build output (ignored)
```

At run time the tool keeps everything next to its own `.exe`:

```
modswitch-order.json   slot list, keeps a disabled mod's place; invalidated if the game folder changes
modswitch-game.txt     the folder you picked by hand (auto-detection is not persisted)
modswitch-lang.txt     a manual language choice ("auto" deletes the file and follows the OS again)
modswitch-backup\      originals, one set per run, plus manifest.txt for undo
modswitch-deleted\     removed and replaced mods
modswitch-unpack\      temporary, only while a 7z/rar is being extracted; removed when done
loader\dinput8.dll     YOUR copy of the loader, used by the Install button
```

## Troubleshooting

| Symptom | What it means |
|---|---|
| Every mod is red / nothing loads | `dinput8.dll` is missing from the game folder. Install DivaModLoader, or put it in `loader\` and press *Install mod loader*. |
| A pack with subfolders shows "will not load" | its `include` array does not name the folders that contain `rom/`. This tool will not guess it for you - fix the pack's own `config.toml`. |
| Rows show 「不在表」/"not listed" (orange) | enabled but absent from `priority`, so the loader reads it in its own order. Press *Check*, then reorder or toggle it once to re-derive the array. |
| An archive is refused as "not an archive this tool reads" | it is not zip/7z/rar, or the header does not match the extension. |
| 7z/rar fails with a tar message | most often an encrypted or damaged archive; the exit code and stderr are shown as they came. |
| Windows SmartScreen warns about the exe | it is unsigned. The full source and a one-command build are here, so you can compile it yourself. |

## Third-party and legal

- Hatsune Miku, Project DIVA, SEGA and Crypton Future Media names and assets belong to their owners.
  This repository contains **no game content and no mods**.
- Mods you install remain the work of their authors; respect their own distribution terms.
- DivaModLoader is not bundled here.
- This tool is unofficial and comes with no warranty; it edits config files of a game you own.

## License

MIT - see [LICENSE](LICENSE).
