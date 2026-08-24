<div align="center">

<img src="src/FateTakesYouHome/Assets/Icons/mark-256.png" width="112" alt="Fate Takes You Home">

# Fate Takes You Home

**Your lights, scenes and automations, one click from the taskbar — so the house is ready before you are.**

A native Windows tray application for Home Assistant. No Chromium, no web view, no background
server. One executable that lives beside the clock.

</div>

---

## What it is

Home Assistant already knows how to run your house. What it does not have is a way to reach it
that costs less than opening a browser tab. This is that: a notification-area icon that opens a
panel next to itself, with the handful of things you actually touch.

- **Click the tray icon** and a panel slides out of it, anchored to the icon, sized to its
  contents.
- **Everything you pinned** is in it — lights with brightness, scenes, scripts, automations,
  covers, thermostats, locks, media.
- **Double click** for the full window: browse every entity Home Assistant knows about, grouped by
  room, and pin what you use.
- **Arrange both surfaces like a phone**: the Layout page is a drag-and-resize widget grid — tiles,
  one-action buttons, room summaries, and history graphs of any numeric sensor.
- **It looks like the house style**, and every colour, corner and animation in it is a value you
  can change. Thirteen themes ship in the box; the sky twinkles, and now and then a star falls.
- **It keeps itself current**: a once-a-day check against this repository's releases offers new
  versions with one click, and never installs without it.

Built with WPF on .NET 8. It talks to Home Assistant over the native WebSocket API — no custom
integration, no HACS component, nothing to install on the server side beyond a Long-Lived Access
Token you create from your own profile page.

<!--
  Screenshots deliberately absent.

  A screenshot of this application is a photograph of somebody's house: entity names, room names,
  the account the automations belong to, the server address, and whatever the sensors happened to
  read at the time. Shots taken against a live instance do not belong in a public repository.

  Any screenshots added here must be captured against fabricated data — the fake Home Assistant
  server in tests/ serves exactly this purpose — and never against a real home.
-->

## Getting started

1. **Install it.** Run the MSI from
   [Releases](https://github.com/VagueDustin/fate-takes-you-home/releases), or unzip the portable
   build anywhere and run `FateTakesYouHome.exe`. Nothing else is required — the .NET runtime is
   included.
2. **Create a token.** In Home Assistant, open your profile → Security → Long-Lived Access Tokens
   → *Create Token*. Copy it; Home Assistant will not show it again.
3. **Point the app at your server.** Settings → server address and token → *Test connection* →
   *Save and connect*.
4. **Pin what you use.** Everything → find it → the pin button.

The app walks you through all four the first time it runs, and Help tracks which are actually done.

## What it can control

| Domain | What you get |
| --- | --- |
| `light` | On/off, brightness, colour temperature, RGB, effects |
| `switch`, `input_boolean` | On/off |
| `scene` | Activate, with optional transition |
| `script` | Run, with variables |
| `automation` | Trigger now, or enable/disable |
| `cover`, `valve` | Open, close, stop, position, tilt |
| `climate` | Mode, target temperature, range, fan mode, preset |
| `fan` | On/off, speed, oscillation, preset |
| `lock` | Lock, unlock |
| `media_player` | Play/pause, next, previous, volume, mute, source |
| `vacuum` | Start, pause, return, locate |
| `number`, `select`, `text` | Set the value |
| `button`, `siren`, `humidifier`, `water_heater` | Domain-appropriate controls |
| `sensor`, `binary_sensor`, `person`, `device_tracker` | Read-only tiles with sensible formatting |

## Themes

The default theme is **FATE** — the VagueDustin house style, navy and gold, at the *charted*
ornament tier. Twelve more ship with it — Daybreak, Light, Crimson, Midnight, Mono, Terminal,
Cyberpunk, Dracula, Nord, Gruvbox, One Dark and Rosé Pine — and you can write your own. The
Appearance page also carries your font choices (they win over every theme) and the system-wide
keyboard shortcuts.

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
backdrop, and every animation duration and easing curve — including how far the panel travels as
it opens and what curve it settles on. There is a full editor with live preview if you would rather
not write JSON, and a contrast checker that holds a theme to WCAG AA.

See **[docs/THEMING.md](docs/THEMING.md)** for the whole reference.

## Command line

| Argument | Effect |
| --- | --- |
| *(none)* | Open the full window |
| `--panel` | Open the tray panel |
| `--tray` | Start into the tray with no window. What the autostart entry uses. |

## Updates

The app checks this repository's [Releases](https://github.com/VagueDustin/fate-takes-you-home/releases)
once a day (Settings → Updates, where it can also be turned off or run on demand). When a newer
version exists it offers one button: download, verify the size against the release manifest, hand
the MSI to Windows Installer, and restart. The check is a single anonymous request; nothing about
you or your house is sent anywhere.

`--panel` works whether or not the app is already running, which is what makes it useful on a
shortcut: put one on your desktop, open its properties, and assign a shortcut key.

## Where things live

| | |
| --- | --- |
| Installed to | `C:\Program Files\VagueDustin Enterprises\Fate Takes You Home` |
| Settings and themes | `%APPDATA%\VagueDustin Enterprises\Fate Takes You Home` |
| Logs | `%LOCALAPPDATA%\VagueDustin Enterprises\Fate Takes You Home\Logs` |

The application never writes to its install directory, so Program Files can stay read-only for
standard users. Your access token is encrypted with Windows data protection and is readable only
by your Windows account — see [SECURITY.md](SECURITY.md).

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
| [CONTRIBUTING.md](CONTRIBUTING.md) | Conventions, and what a good change looks like here |
| [SECURITY.md](SECURITY.md) | Threat model, token handling, reporting a problem |

## Licence

GNU Affero General Public License, version 3 or later. See [LICENSE](LICENSE).

The AGPL is deliberate. This is a client for a self-hosted, free-software home automation system,
and anyone who runs a modified version as a service should publish their changes.

Bundled typefaces — Inter, Cinzel and Crimson Pro — are under the SIL Open Font License 1.1 and
remain so; see [their licences](src/FateTakesYouHome/Assets/Fonts/).

---

<div align="center">
<sub>Provided by VagueDustin Enterprises™ · © 2026 Fate Takes You Home. All rights reserved.</sub>
</div>
