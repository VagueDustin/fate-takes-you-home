<div align="center">

<img src="src/FateTakesYouHome/Assets/Icons/mark-256.png" width="112" alt="Fate Takes You Home">

# Fate Takes You Home

**Your lights, scenes and automations, one click from the taskbar, so the house is ready before you are.**

A native Windows tray application for Home Assistant. No Chromium, no web view, no background
server. One executable that lives beside the clock.

<a href="https://github.com/VagueDustin/fate-takes-you-home/releases/latest"><img alt="Download the latest release" src="https://img.shields.io/github/v/release/VagueDustin/fate-takes-you-home?style=for-the-badge&label=Download&labelColor=16213E&color=D4AF37"></a>

<a href="https://github.com/VagueDustin/fate-takes-you-home/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/VagueDustin/fate-takes-you-home?label=release&labelColor=16213E&color=D4AF37"></a>
<a href="https://github.com/VagueDustin/fate-takes-you-home/releases/latest"><img alt="Release date" src="https://img.shields.io/github/release-date/VagueDustin/fate-takes-you-home?label=released&labelColor=16213E&color=D4AF37"></a>
<a href="https://github.com/VagueDustin/fate-takes-you-home/releases"><img alt="Total downloads" src="https://img.shields.io/github/downloads/VagueDustin/fate-takes-you-home/total?label=downloads&labelColor=16213E&color=D4AF37"></a>
<a href="https://github.com/VagueDustin/fate-takes-you-home/actions/workflows/build.yml?query=branch%3Amain"><img alt="Build status on main" src="https://img.shields.io/github/actions/workflow/status/VagueDustin/fate-takes-you-home/build.yml?branch=main&label=build&labelColor=16213E"></a>
<a href="LICENSE"><img alt="Licence" src="https://img.shields.io/github/license/VagueDustin/fate-takes-you-home?label=licence&color=D4AF37&labelColor=16213E"></a>
<img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-D4AF37?labelColor=16213E">
<img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-D4AF37?labelColor=16213E">

**MSI** for a normal install with Start menu entry and one-click updates ·
**portable zip** to run from any folder without installing

</div>

---

## What it is

Home Assistant already knows how to run your house. What it does not have is a way to reach it
that costs less than opening a browser tab. This is that: a notification-area icon that opens a
panel next to itself, with the handful of things you actually touch.

- **Click the tray icon** and a panel slides out of it, anchored to the icon, sized to its
  contents. Everything you pinned is in it: lights, switches, scenes, scripts, automations,
  covers, locks, media players and sensors.
- **Double click** for the full window: browse every entity Home Assistant knows about, grouped by
  room, search it by name, entity id or room, and pin what you use.
- **Arrange both surfaces like a phone.** The Layout page is a drag-and-resize widget grid. A
  widget can be an entity's tile, a one-action button (press an entity, or turn every light off),
  the rooms grid with lit rooms first, the whole-house activity counts, or a history graph of any
  numeric sensor.
- **Keys that work from anywhere.** The Shortcuts page holds the system-wide keys: four for the
  app itself (open the panel, open the window, all lights off, run your default pin) and as many
  device shortcuts as you like. A device shortcut switches, dims, pauses or mutes the devices you
  choose, singly or as a group that acts as one.
- **It looks like the house style**, and every colour, corner and animation in it is a value you
  can change. Thirteen themes ship in the box; the sky twinkles, and now and then a star falls.
- **It keeps itself current**: a once-a-day check against this repository's releases offers new
  versions with one click, and never installs without it.

Built with WPF on .NET 10. It talks to Home Assistant over the native WebSocket API. No custom
integration, no HACS component, nothing to install on the server side beyond a Long-Lived Access
Token you create from your own profile page.

<!--
  Screenshots deliberately absent.

  A screenshot of this application is a photograph of somebody's house: entity names, room names,
  the account the automations belong to, the server address, and whatever the sensors happened to
  read at the time. Shots taken against a live instance do not belong in a public repository.

  Any screenshots added here must be captured against fabricated data (the fake Home Assistant
  server in tests/ serves exactly this purpose) and never against a real home.
-->

## Requirements

- **Windows 10 (1809 or later) or Windows 11**, 64-bit. Both downloads carry their own .NET
  runtime, so there is nothing else to install.
- **A Home Assistant instance this PC can reach**, by IP address, hostname, or through a reverse
  proxy that passes WebSockets.
- **A Long-Lived Access Token** from a Home Assistant user. The app can do whatever that user can.

## Getting started

1. **Install it.** Download from the
   [latest release](https://github.com/VagueDustin/fate-takes-you-home/releases/latest) and run
   the MSI, or unzip the portable build anywhere and run `FateTakesYouHome.exe`.
2. **Create a token.** In Home Assistant, open your profile → Security → Long-Lived Access Tokens
   → *Create Token*. Copy it; Home Assistant will not show it again.
3. **Point the app at your server.** Settings → server address and token → *Test connection* →
   *Save and connect*.
4. **Pin what you use.** Everything → find it → the pin button.

The app walks you through all four the first time it runs, and Help tracks which are actually done.

## Installing, upgrading, removing

| | MSI | Portable zip |
| --- | --- | --- |
| File | `FateTakesYouHome-<version>-win-x64.msi` | `FateTakesYouHome-<version>-portable-win-x64.zip` |
| Goes to | `C:\Program Files\VagueDustin Enterprises\Fate Takes You Home` | Wherever you unzip it |
| Needs | An administrator prompt, once per install or upgrade | Nothing |
| Start menu entry | Yes | No |

**Upgrading.** Accept the update the app offers, or run a newer MSI over the old one: it replaces
the installed version in place, closing the running copy first. A portable copy is upgraded by
replacing its folder. The in-app update always installs the MSI, so a portable copy that accepts
one becomes an installed copy; to stay portable, turn *Check for updates* off in Settings.

**Start with Windows** is a setting inside the app (Settings → *Start with Windows*), not part of
the installer. It writes one value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for
your account only, needs no administrator rights, and starts the app with `--tray`. It works for
the portable build too, and repairs itself if you move the folder.

**Removing.** Turn *Start with Windows* off first, then uninstall from Settings → Apps in Windows
(or delete the portable folder). The uninstaller does not touch your profile, so your settings,
themes and logs stay where they are (see [Where things live](#where-things-live)); delete those
folders too if you want nothing left behind. The Run value is per-user, which is why the
per-machine uninstaller does not remove it.

## What it can control

Each pinned entity gets a tile. Tapping the tile does one thing, and some tiles carry a slider
underneath.

| Domain | Tapping the tile | Also on the tile |
| --- | --- | --- |
| `light` | On/off | Brightness slider, for lights that dim |
| `switch`, `input_boolean`, `siren`, `humidifier`, `water_heater` | On/off | |
| `fan` | On/off | Speed slider, for fans with speeds; the preset when one is set |
| `scene` | Activate | |
| `script` | Run | Whether it is running |
| `automation` | Trigger it now, skipping its conditions | Whether it is enabled |
| `button`, `input_button` | Press | |
| `cover`, `valve` | Open when closed, close when open | Position slider, when the device reports one |
| `lock` | Lock or unlock | |
| `climate` | On/off | Mode, current and target temperature |
| `media_player` | Power on/off | Volume slider; what is playing |
| `vacuum` | Start, or send it home while it is cleaning | |
| `number`, `input_number`, `select`, `input_select` | Nothing yet | The current value |
| `sensor`, `binary_sensor`, `person`, `device_tracker`, `camera`, `update`, `todo` | Read-only | The state, formatted for people (21.5 °C, Open, Detected, Home) |

Device shortcuts reach a little further: play/pause, pause, next, previous, volume steps and mute
for media players, brightness steps or a set level for lights, and on, off or toggle for most of
the domains above, which is how an automation is enabled or disabled from the app. A shortcut can
lock a door but never unlock one.

Plenty of what Home Assistant can do has no control here yet: colour temperature, colour and
effects on lights, scene transitions, script variables, cover tilt, climate modes, temperatures,
fan modes and presets, fan oscillation and presets, media sources, and setting a number or a
select. Use Home Assistant itself for those. `text` and `input_text` entities are not shown.

## Themes

The default theme is **FATE**, the VagueDustin house style, navy and gold, at the *charted*
ornament tier. Twelve more ship with it (Daybreak, Light, Crimson, Midnight, Mono, Terminal,
Cyberpunk, Dracula, Nord, Gruvbox, One Dark and Rosé Pine), and you can write your own. The
Appearance page also carries your font choices (they win over every theme).

A theme is a **patch**, not a document. It declares only what it changes and inherits the rest, so
the smallest useful theme is six lines:

```json
{
  "id": "brass",
  "name": "Brass",
  "basedOn": "fate",
  "colors": { "accentDefault": "#B08D57" }
}
```

Drop that in `%APPDATA%\VagueDustin Enterprises\Fate Takes You Home\Themes\` and it appears in the
picker within a second. Edit it while the app is running and the interface repaints as you save.

Themes control colours, typography, corner radii, button style, ornament density, the window
backdrop, and every animation duration and easing curve, including how far the panel travels as
it opens and what curve it settles on. There is a full editor with live preview if you would rather
not write JSON, and a contrast checker that holds a theme to WCAG AA.

See **[docs/THEMING.md](docs/THEMING.md)** for the whole reference.

## Command line

| Argument | Effect |
| --- | --- |
| *(none)* | Open the full window |
| `--panel` | Open the tray panel |
| `--tray` | Start into the tray with no window. What the autostart entry uses. |

`--panel` works whether or not the app is already running, which is what makes it useful on a
shortcut: put one on your desktop, open its properties, and assign a shortcut key.

## Updates

The app checks this repository's [Releases](https://github.com/VagueDustin/fate-takes-you-home/releases)
once a day (Settings → Updates, where it can also be turned off or run on demand). When a newer
version exists it offers one button: download, verify the size against the release manifest, hand
the MSI to Windows Installer, and restart. The check is a single anonymous request; nothing about
you or your house is sent anywhere.

What changed in each version is in **[CHANGELOG.md](CHANGELOG.md)**.

## Privacy

- **It talks to two places**: the Home Assistant address you give it, and, once a day unless you
  turn it off, GitHub's public releases API to look for an update. No telemetry, no analytics, no
  account, and it opens no listening ports.
- **Your access token is encrypted** with Windows data protection, readable only by your Windows
  account on this PC. A copied or backed-up settings file does not carry a usable token, and the
  token never appears in the log or on screen beyond its last four characters.
- **Certificates are checked** on `https` and `wss` addresses. *Accept self-signed certificates*
  turns that off and is meant only for a network and a certificate you control.

[SECURITY.md](SECURITY.md) has the threat model and how to report a problem.

## Where things live

| | |
| --- | --- |
| Installed to | `C:\Program Files\VagueDustin Enterprises\Fate Takes You Home` |
| Settings and themes | `%APPDATA%\VagueDustin Enterprises\Fate Takes You Home` |
| Logs | `%LOCALAPPDATA%\VagueDustin Enterprises\Fate Takes You Home\Logs` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, only when enabled |

The application never writes to its install directory, so Program Files can stay read-only for
standard users.

## When it does not connect

The message the app shows usually names the problem. The common ones, with fixes, are in
**[docs/HOME-ASSISTANT.md](docs/HOME-ASSISTANT.md#troubleshooting)**:

- *"That address answered, but not with a WebSocket"*: the wrong port, or something other than
  Home Assistant at that address.
- *"The server refused the WebSocket upgrade"* or a connection that drops every minute: a reverse
  proxy that needs its WebSocket settings.
- *"Home Assistant rejected the access token"*: revoked, or pasted incompletely.
- Connected, but nothing to pin: the filters for unavailable and diagnostic entities.

For anything else, Help → *Open the log*. Verbose logging (Settings → Files and diagnostics)
records every message exchanged with Home Assistant, never the token.

## Building it

```bash
dotnet test
./build/publish.ps1
```

That produces a self-contained publish, a portable zip and an MSI in `artifacts/`. Full detail in
**[docs/BUILDING.md](docs/BUILDING.md)**.

## Documentation

| | |
| --- | --- |
| [docs/THEMING.md](docs/THEMING.md) | Every theme option, with examples |
| [docs/HOME-ASSISTANT.md](docs/HOME-ASSISTANT.md) | How the connection works, and what to do when it does not |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the code is put together and why |
| [docs/BUILDING.md](docs/BUILDING.md) | Building, packaging, releasing |
| [CHANGELOG.md](CHANGELOG.md) | What changed in each release |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Conventions, and what a good change looks like here |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | How people are expected to treat each other here |
| [SECURITY.md](SECURITY.md) | Threat model, token handling, reporting a problem |

## Licence

GNU Affero General Public License, version 3 or later. See [LICENSE](LICENSE).

The AGPL is deliberate. This is a client for a self-hosted, free-software home automation system,
and anyone who runs a modified version as a service should publish their changes.

The bundled typefaces (Inter, Cinzel and Crimson Pro) are under the SIL Open Font License 1.1 and
remain so; see [their licences](src/FateTakesYouHome/Assets/Fonts/).

---

<div align="center">
<sub>Provided by VagueDustin Enterprises™ · © 2026 Fate Takes You Home. All rights reserved.</sub>
</div>
