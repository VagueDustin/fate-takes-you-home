# Changelog

Notable changes to Fate Takes You Home. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

### Fixed

- **Test connection could fail against a real server with "Identifier values have to increase".**
  Home Assistant refuses any frame whose id is not greater than the last it saw on the connection.
  The id was allocated outside the send lock, so two overlapping commands could take 1 and 2, swap
  places waiting for the lock, and put 2 on the wire first — the server then refused the lower id
  with `id_reuse`. The id is now taken inside the lock, which is the only place the number and the
  write can be made a single step. Reported by a first run on a fresh install, where *Test
  connection* raced the initial `subscribe_events`.

### Changed

- The fake Home Assistant server in the tests now enforces the real server's monotonic-id rule, so
  a client that lets two sends race fails the suite instead of only failing against a real house.

## [0.3.0] — 2026-08-24

The make-it-yours release: the app updates itself, both surfaces became arrangeable widget grids,
the theme set tripled behind a picker that shows every theme in its own colours, and the sky
finally moves.

### Added

- **Auto-update from GitHub releases.** Once a day (and on demand from Settings → Updates) the app
  asks the public releases API whether a newer version exists. If one does, a gold banner offers
  "Install and restart" — download, integrity check against the release's stated size, and a
  hand-off to msiexec. Nothing ever installs without the click, and the check can be turned off.
- **A layout editor** (the new Layout page): arrange the home screen and the tray panel like a
  phone launcher — drag to move, pull the corner grip to resize, snap to a fluid grid. Widgets:
  entity tiles, one-action buttons, the rooms grid, the activity counts, and **history graphs** of
  any numeric sensor (recorder history over the WebSocket API, refreshed every ten minutes).
  Layouts live in settings as plain JSON; "Back to standard" forgets them.
- **Nine new built-in themes** — Light, Crimson, Terminal (green phosphor, hard corners, mono
  everything), Cyberpunk (violet dark, hot neon), Dracula, Nord, Gruvbox, One Dark and Rosé Pine —
  joining FATE, Daybreak, Midnight and Mono. Thirteen in the box; FATE Ceremonial and FATE Charted
  retired from the presets (the tier system remains for custom themes).
- **The theme picker shows the themes.** Each is a card sketching itself in its own surface, text
  and accent colours, with the applied one carrying a check in its own accent — chosen by eye now,
  not by name.
- **Fonts, yours across every theme**: display, body and mono pickers on the Appearance page,
  offering the bundled faces and everything installed on the machine.
- **System-wide keyboard shortcuts**, recorded by pressing them: open the panel, open the window,
  all lights off, run the default pin. Registered through RegisterHotKey, so a combination another
  app owns is reported as taken instead of silently dead.
- **The sky moves.** A dozen stars twinkle over the static field, and every half minute or so one
  falls. Storyboard-driven, a handful of elements, honours reduced-motion and the theme's motion
  switch, and stops entirely while the window is hidden.
- **Back and forward, everywhere.** The mouse's back/forward buttons, Alt+Left/Right, and a back
  button in the title bar all walk the page history — including the room-click filter, which used
  to be a dead end. The search box also grew an inline clear button.

### Changed

- **The installer wears the house style**: navy starfield, the gold arch and wordmark on the
  welcome and finish pages, a branded banner on the rest — drawn at build time from the same
  palette constants as the app.
- The window adapts to its size: the pinned grid runs one, two or three columns by available
  width, and below 980px the navigation rail collapses to icons.

### Fixed

- **The installer launched the app before Finish was clicked** — the launch action was scheduled
  after InstallFinalize, which runs while the exit dialog is still on screen, and unticking the
  checkbox did nothing because the sequence had already read it. The launch now fires from the
  Finish button itself, as the non-elevated user, with the path properly quoted.
- **Midnight's tray panel wore a dark box.** The acrylic blur is applied by an accent policy that
  paints the entire window rectangle — including the transparent 28px shadow frame around the
  panel. In acrylic mode the frame now collapses to nothing, the WPF drop shadow retires, and DWM
  rounds the actual window to match the panel.
- **Clicking away did not always dismiss the panel.** Dismissal hung off window deactivation, and
  clicking the bare desktop or taskbar activates nothing — after a quick reopen the panel held the
  foreground and nothing short of the tray icon would close it. A low-level mouse hook now watches
  for any press outside the panel (and outside the tray icon, whose click has its own meaning)
  while it is open — the same mechanism the shell's own flyouts use — and exists only while the
  panel is visible.

## [0.2.0] — 2026-08-23

The cohesion release. The 0.1 window was a grid of nested grey boxes that happened to sit on a
starfield; this one is a single night sky with panels floating on it, and everything from the
caption buttons to the pin control was redrawn to match. Several silent-failure paths found on a
real install now speak up instead.

### Added

- **Rooms on the dashboard.** Every area that has lights, lit rooms first, each saying how many
  are on — the honest ones say "Dark". Clicking a room opens Everything filtered to that room.
- **A quick-action footer in the tray panel**: "All lights off", with the result reported in
  place. The panel also gained the same starfield backdrop as the full window.
- **A live log viewer** under Settings → Files and diagnostics, fed from the in-memory tail, with
  a copy button. It keeps working even when the log file itself cannot be written — which is
  exactly when it is needed — and says plainly whether entries are reaching disk.
- **A visible warning when settings cannot be saved.** A failing settings write previously logged
  one line and otherwise let every pin, theme choice and switch flip vanish on exit. It is now a
  banner in the window, with a retry button, and it withdraws itself when writes land again.
- **Colour swatches in the theme picker.** Each theme's own surface, overlay, accent and text
  colours appear under its name, and the applied theme carries a gold check — choosing no longer
  requires trying each one.

### Changed

- **The window is one surface.** Page panels are now a translucent wash over the backdrop instead
  of opaque slabs, the navigation rail sits directly on the sky with the connection status at its
  foot, the centred title-bar status line is gone, and the caption buttons are frameless with the
  conventional red close hover (drawn in the theme's danger colour).
- **Entity rows were redrawn.** The black square icon tiles became engraved circular wells; a lit
  entity's well fills, gilds and glows. Momentary entities — scenes, scripts, buttons — show a
  chevron run cue instead of an inert dot, and it answers hover in gold. The pin control in the
  browser is a quiet circular toggle instead of a boxed button.
- **Group headings in the browser** carry their count beside the name with a rule running to the
  edge, instead of a stray number under the scrollbar.
- **Words instead of enum names.** The tray-action dropdowns say "Open the full window" and "Run
  the default pin" rather than `OpenMainWindow` and `RunDefaultAction`; grouping says "By room".
  Pin rename boxes show the server's name as a watermark so an empty box still says what it is.
- "Turn off all lights" on the dashboard is styled as the page's primary action.
- The welcome page's mark is drawn without its dark plate, on its halo alone.

### Fixed

- **Relaunching the app while it was running did nothing.** The single-instance activation
  listener was a message-only window, and Windows does not deliver `HWND_BROADCAST` messages to
  message-only windows — so the "wake the running copy" message was shouted into a void. The
  listener is now a hidden ordinary window; a second launch reliably opens the full window, and
  `--panel` opens the tray panel.
- **A plain launch now always opens the window.** Previously an onboarded user double-clicking
  the executable got a tray icon and nothing else, which read as the app failing to start.
- **The welcome page no longer reappears for configured installs.** A working connection now
  counts as onboarding complete; the four-step quickstart tracked this correctly but the welcome
  gate never learned.
- **Zero theme files loading is no longer silent.** One real install came up at sign-in painting
  the compiled fallback while claiming to be FATE, with nothing in the log. The condition is now
  logged as a warning, retried after five seconds, and the fallback baseline itself resolves to
  the true FATE values either way.
- The theme detail pane no longer shows an empty "Based on" row for root themes.

## [0.1.1] — 2026-08-23

Fixes from the first real install, against a large Home Assistant instance.

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
