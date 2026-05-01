# Krile Helper — Linux

Native Linux port of [Tataru Helper](https://github.com/NightlyRevenger/TataruHelper),
renamed to **Krile Helper** for the Linux fork. A real-time chat translation
overlay for **Final Fantasy XIV** running under Proton/Wine. No Wine-side
injection, no DLL hooks: reads game memory directly from the host using
`process_vm_readv` plus the canonical Sharlayan signatures.

> **This is a Linux-exclusive fork** of the upstream Windows project — WPF +
> .NET Framework 4.6.x + Sharlayan via `kernel32.ReadProcessMemory` — at
> [NightlyRevenger/TataruHelper](https://github.com/NightlyRevenger/TataruHelper).
> All Windows-only code (WPF UI, BondTech hotkeys, NotifyIconWpf tray, Squirrel
> updater, original Sharlayan + Translation projects) was removed in this fork.
> Credit for the original concept, signatures, chat decoding pipeline, and
> Tataru artwork goes to that project and its contributors.

## Architecture

| Project | Target | Purpose |
|---|---|---|
| [`Sharlayan.Core`](Sharlayan.Core/) | net8.0 | Cross-platform memory reader. `INativeMemory` interface with a Linux backend (Windows backend is straightforward to add). Handles process discovery, PE parsing, signature scanning, pointer resolution, chat-log decoding. |
| [`Translation.Core`](Translation.Core/) | net8.0 | Pluggable translation backends. Ships with `GoogleFreeTranslator` (free unauthenticated endpoint) and `DeepLTranslator` (Free/Pro tiers). |
| [`KrileHelper.UI`](KrileHelper.UI/) | net8.0 (Avalonia 11) | Floating chat overlay with channel filtering, settings, persistence. |
| [`MemoryProbe`](MemoryProbe/) | net8.0 | CLI diagnostic tool — prints attached process info + signature scan results + recent chat lines. |

## Build & run from source

Requires .NET 8 SDK (`pacman -S dotnet-sdk-8.0` on Arch).

```sh
cd KrileHelper.UI
dotnet run -c Release
```

Diagnostic CLI:

```sh
cd MemoryProbe
dotnet run -c Release
```

Run tests:

```sh
dotnet test KrileHelper.sln -c Release
```

## Packaging — single-file binary

```sh
scripts/publish.sh                # produces dist/linux-x64/krile-helper (~44 MB)
scripts/publish.sh linux-arm64    # for ARM
```

## Install for the current user

```sh
cd dist/linux-x64
./install.sh
```

Drops:

- `~/.local/bin/krile-helper`
- `~/.local/share/icons/hicolor/256x256/apps/krile-helper.png`
- `~/.local/share/applications/krile-helper.desktop`

After install, "Krile Helper" appears in your application menu, or run
`krile-helper` from a terminal (assuming `~/.local/bin` is on `$PATH`).

To uninstall: run `./uninstall.sh` from the dist directory. User config at
`~/.config/krile-helper/` and signature cache at `~/.cache/sharlayan-core/`
are preserved.

## Runtime requirements

- A graphical session (X11 or Wayland)
- Standard desktop libs (fontconfig, libxkbcommon, libx11, libxrandr, libxi —
  installed by default on every desktop distro)
- FFXIV running under Proton/Wine on the same machine, owned by the same user

## Wayland + fullscreen FFXIV

Wayland forbids any app from drawing over an exclusive-fullscreen surface, by
design. **Switch FFXIV to "Borderless Windowed"** (System → Display) and the
overlay will sit on top correctly. Under X11 this is not an issue.

## Translation backends

- **Google Translate (free)** — works out of the box, no key. Quality is decent
  for short chat lines; rate-limits aggressive callers.
- **DeepL** — better quality, especially for prose. Get a free API key at
  https://www.deepl.com/pro-api (DeepL API Free, 500K chars/month, requires a
  card for verification but no charges). Paste the key in Settings → Engine →
  DeepL. Keys ending in `:fx` use the Free endpoint automatically.

Translation sends enabled chat lines to the selected third-party backend. To
reduce accidental disclosure, private/social channels such as tells, party,
Free Company, linkshells, CWLS, alliance, and Novice Network are shown but not
translated by default; enable them explicitly in Settings if desired.

## Configuration

All settings live at `~/.config/krile-helper/settings.json` and are written
automatically when toggled in the in-app Settings dialog (⚙ button in the
title bar). Hand-editing is fine if you prefer.

Global overlay toggling is enabled by default with `Ctrl+Alt+Space`:

```json
{
  "Hotkeys": {
    "ToggleOverlayEnabled": true,
    "ToggleOverlayShortcut": "Ctrl+Alt+Space"
  }
}
```

Under X11 this uses `XGrabKey`. Under Wayland it first tries the
`org.freedesktop.portal.GlobalShortcuts` portal, which may prompt for user
approval and depends on compositor/portal support; if unavailable, Krile Helper
falls back to X11/XWayland when `DISPLAY` is present.

## Status & known limitations

| | |
|---|---|
| Chat log (post-dialog) | ✅ Works |
| Translation overlay | ✅ Works |
| Real-time NPC dialog panel | ❌ — Sharlayan signatures for `DIALOGPANEL_*` were dropped from upstream resources because they break each major patch. Lines appear in the overlay only after you advance past the in-game text panel. |
| Global hotkeys | ✅ — `Ctrl+Alt+Space` toggles the overlay; X11 native, Wayland via GlobalShortcuts portal when supported |
| System tray | ✅ — Avalonia tray icon backed by Linux StatusNotifier/DBus; GNOME may require an AppIndicator/KStatusNotifier extension |

## License

MIT — see [LICENSE](LICENSE). The original copyright (NightlyRevenger 2019)
is preserved.
