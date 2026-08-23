# Changelog

Notable changes to Fate Takes You Home. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] — 2026-08-23

First release.

### Added

**The tray panel**

- A notification-area icon that opens a panel anchored to itself, sized to its contents.
- Placement handles all four taskbar edges, auto-hide, multiple monitors and mixed DPI.
- The panel slides out of the taskbar edge, growing from the corner nearest the icon, on a
  theme-defined cubic-Bézier curve.
- Single click opens the panel. Double click and middle click are configurable.
- A `--panel` argument that works whether or not the app is already running, so a desktop shortcut
  can carry a hotkey.

**Home Assistant**

- Native WebSocket client: authentication, live `state_changed` subscription, exponential-backoff
  reconnection, application-level keepalive. No server-side component of any kind.
- Reads the area, floor, device and entity registries, so entities group by room the way they do in
  Home Assistant's own interface.
- Controls for lights (brightness, colour temperature, RGB, effects), switches, scenes, scripts,
  automations, covers, valves, climate, fans, locks, media players, vacuums, numbers, selects, text,
  buttons, sirens, humidifiers and water heaters. Read-only tiles for sensors, binary sensors,
  people and device trackers.
- Optimistic toggles and debounced sliders, so the interface responds to the click rather than to
  the network.
- Access token encrypted with Windows data protection.

**The full window**

- Dashboard with pinned entities and an account of what is currently on.
- Entity browser with search across name, entity id and area, grouped by area, floor or domain.
- Settings for connection, startup, tray gestures, pin order and diagnostics.
- Connection test reporting the server version, the account and the entity count.
- Quickstart that tracks which steps are genuinely done, and a guided tour with spotlight coach
  marks that walks across pages.

**Theming**

- A full theme SDK. Themes are JSON patches that declare only what they change and inherit the
  rest, with `basedOn` inheritance, cycle detection and a published JSON Schema.
- Control over colours, typography, shape, motion, ornament density, button style and window
  backdrop — including animation durations, travel distance and easing curves.
- Hot reload: edit a theme file while the app runs and the interface repaints as you save.
- An in-app editor with live preview across the whole application, plus import and export.
- A validator enforcing WCAG AA contrast and the house rule that the accent means "interactive".
- Six themes: FATE, FATE Ceremonial, FATE Utility, Midnight, Daybreak and Mono.

**Packaging**

- Self-contained build; no .NET runtime prerequisite.
- MSI installing to `C:\Program Files\VagueDustin Enterprises\Fate Takes You Home`, with upgrade
  handling, downgrade protection, and Start Menu shortcuts for both the window and the panel.
- Portable zip that shares its configuration with an installed copy.
- Per-user autostart needing no elevation, repaired automatically if the executable moves.

### Known limitations

- **x64 only.** There is no ARM64 build yet.
- **Unsigned.** SmartScreen will warn on first run until the binaries are code-signed.
- **`mica` backdrop applies to the full window only.** The tray panel is a layered window so the
  app can composite and animate it, and the DWM backdrop cannot draw behind one. Use `acrylic`
  there.
- **No variable font support**, because WPF has none. The bundled faces are static instances.
- Themes change how things look, not what is there. There is no plugin surface for new controls.

[Unreleased]: https://github.com/VagueDustin/fate-takes-you-home/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/VagueDustin/fate-takes-you-home/releases/tag/v0.1.0
