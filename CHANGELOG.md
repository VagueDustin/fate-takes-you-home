# Changelog

Notable changes to Fate Takes You Home. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

## [0.1.1] — 2026-08-23

Fixes from the first real install, against a 1057-entity Home Assistant.

### Fixed

- **The tray panel opened and closed again immediately.** Windows only grants foreground rights to
  a process that received the last input event, and when you click a tray icon that process is
  Explorer. `SetForegroundWindow` was therefore refused, the panel appeared unfocused, and WPF
  raised `Deactivated` — which the panel treated as a click elsewhere and dismissed itself. It now
  takes the foreground through the documented `AttachThreadInput` route and disregards a
  deactivation for 450 ms after opening, re-asserting the foreground instead of hiding. A repeated
  tray notification is also coalesced, since some shells deliver two for one click.
- **The entity browser realised every row.** `ScrollViewer`'s content presenter defaults
  `CanContentScroll` to false whatever the `ScrollViewer` says, and the custom template neither
  bound it nor named the presenter `PART_ScrollContentPresenter`. Pixel scrolling silently disables
  virtualisation, so a correctly configured `VirtualizingStackPanel` above it was still building
  all 253 visible rows — and roughly 700 with diagnostics shown. Now 7 of 253 are realised, and
  working set dropped from 317 MB to 214 MB.
- **The nested items controls could not virtualise at all.** The browser was a list of groups each
  containing a list of entities; WPF cannot virtualise that shape. It is now one flat list of
  interleaved headers and rows with recycling.
- **State events starved the render loop.** They were queued at `DispatcherPriority.DataBind`,
  which is *higher* than `Render`, so a burst from a busy server froze the window until it cleared.
  Now `Background`.
- **The dashboard rescanned every entity six times per state event**, each scan over a freshly
  allocated snapshot. One pass, no allocation, debounced, and the counts only rebuild when a number
  actually moves.
- **Pinned tiles stretched to half the window.** A `ScrollViewer` arranges content to at least the
  viewport height and `UniformGrid` divides whatever height it is given among its rows.
- **A declared tier no longer inherits an ancestor's ornament switches.** The light Daybreak theme,
  tier utility, was rendering a star field because its FATE parent enabled one explicitly.
  Declaring a tier now resets the switches; a theme's own choices still win.
- `TrackedTextBlock` formatted every glyph twice per layout pass. The run is now built once and
  cached.
- The window background and film grain brushes are rasterised once rather than re-rendered per
  paint.

### Changed

- **The interface follows the FATE reference product much more closely.** A rendered star field
  with constellations behind the full window; section labels in gold rather than grey; a
  gold-outlined pill for the selected navigation item; a distinct gold-outlined primary button; a
  real halo behind the mark; a larger, widely tracked Cinzel wordmark.
- FATE drops corner brackets and film grain — redundant once there is a star field, and fussy
  beside it. `FATE Charted` is a new theme for anyone who wants the full bracketed tier look.
- New `starfield` ornament switch, on by default at the ceremonial and charted tiers.
- The entity browser logs how many rows it realised when verbose logging is on, so losing
  virtualisation again would be visible rather than merely slow.

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

**Testing**

- 186 tests, including a fake Home Assistant WebSocket server that the real client is driven
  against — covering the handshake ordering, a rejected token being terminal, event delivery,
  reconnection after a dropped socket, keepalive, and reply-to-command matching under concurrency.

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

[Unreleased]: https://github.com/VagueDustin/fate-takes-you-home/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/VagueDustin/fate-takes-you-home/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/VagueDustin/fate-takes-you-home/releases/tag/v0.1.0
