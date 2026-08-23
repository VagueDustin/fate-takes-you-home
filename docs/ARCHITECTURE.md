# Architecture

How the code is put together, and the reasoning behind the parts that are not obvious.

- [Shape](#shape)
- [Why WPF](#why-wpf)
- [The tray icon](#the-tray-icon)
- [Placing the flyout](#placing-the-flyout)
- [Animation](#animation)
- [The theme pipeline](#the-theme-pipeline)
- [The Home Assistant client](#the-home-assistant-client)
- [Startup and shutdown](#startup-and-shutdown)
- [Things that look wrong but are not](#things-that-look-wrong-but-are-not)

---

## Shape

```
src/
  Shared/FateMark.cs               The brand mark. Linked into two projects, not referenced.
  FateTakesYouHome.HomeAssistant/  WebSocket + REST client. No UI, no Windows dependencies.
  FateTakesYouHome.Theming/        Theme model, loader, validator, WPF resource renderer.
  FateTakesYouHome/                The application.
    Interop/                       P/Invoke, tray icon, screen placement, window effects
    Services/                      Settings, log, themes, connection, tray, autostart
    ViewModels/                    One per page, plus the entity tile
    Views/                         Windows and pages
    Controls/                      Tracked text, corner brackets, entity glyphs
    Animation/                     Theme-driven motion
    Onboarding/                    The guided tour
build/                             Icon generator, publish script, capture helpers
installer/wix/                     The MSI
themes/                            Shipped themes and the JSON schema
tests/                             xUnit
```

The two libraries have no reference to the application, and `FateTakesYouHome.HomeAssistant` has no
reference to WPF or to Windows. Both are usable on their own; the tests exercise them directly.

Dependencies run one way only: `App → Theming`, `App → HomeAssistant`. The two libraries do not
know about each other.

## Why WPF

The brief was native Windows without Chromium. That leaves WPF, WinUI 3, and hand-rolled Win32.

WinUI 3 is the more modern framework and would have been the obvious pick for a normal desktop app.
It is the wrong pick for this one, because the three things this application is made of are exactly
the three things WinUI 3 is worst at: notification-area icons, positioning a window at an arbitrary
screen coordinate, and an animated transparent popup. All three are either unsupported or require
dropping to the same Win32 calls WPF would need anyway — at which point WPF's mature composition,
per-monitor DPI handling and resource system are pure gain.

.NET 8 rather than 9: it is the LTS release, and nothing here needs anything newer.

The build is **self-contained**. Asking somebody to find and install the .NET Desktop Runtime before
a tray utility will run is a bad first five minutes, and the size difference is a download rather
than a decision.

## The tray icon

`Interop/TrayIcon.cs` drives `Shell_NotifyIcon` directly against a message-only window.

WPF has no tray icon, and the WinForms `NotifyIcon` cannot tell you where it is. That last part is
the whole reason for hand-rolling it: **`Shell_NotifyIconGetRect`** returns the icon's rectangle in
screen pixels, and anchoring the panel to the icon the user actually clicked — rather than to the
corner of the screen — is the difference between feeling native and feeling approximate.

Two details that are easy to get wrong:

- The icon opts in to **`NOTIFYICON_VERSION_4`**, which changes the callback to deliver screen
  coordinates and the `NIN_*` notifications. Everything about placement depends on it.
- Explorer restarting destroys every tray icon and broadcasts **`TaskbarCreated`**. Without
  handling that message the app silently loses its icon whenever Explorer crashes.

The icon is registered by numeric id rather than by GUID. A GUID registration is tied to the
executable path, so moving or reinstalling the app breaks it in a way that is very hard to
diagnose — the icon simply never appears again.

## Placing the flyout

`Interop/ScreenPlacement.cs`. Everything is in **physical pixels**.

WPF's `Window.Left`/`Top` are device-independent and interpreted against the window's *current*
monitor. On a mixed-DPI desktop, setting them to move a window *to* a different monitor puts it in
the wrong place — this is the classic bug where a flyout lands half off the screen. Positioning
through `SetWindowPos` in physical pixels sidesteps the whole problem.

The sequence when the panel opens:

1. Ask the shell where the taskbar is and which edge it is docked to.
2. Find the monitor containing the tray icon, and **that monitor's** DPI.
3. Lay the panel out, measure it, and convert to physical pixels using the destination scale.
4. Compute the panel position: centred on the icon along the bar, at the theme's margin from it.
5. Clamp the panel inside the work area.
6. Position the **window** around that, larger by the shadow frame on every side.

Step 6 matters. The window is bigger than the visible panel because a drop shadow needs somewhere
to render. Treating that empty margin as part of the panel pushes the panel a shadow-width away
from the taskbar — a theme asking for a 12px gap silently gets 28. Placing the panel and deriving
the window from it means the shadow simply overhangs, which is what a shadow should do.

An **auto-hidden taskbar** leaves the work area covering the whole screen, so the bar's thickness
is reserved explicitly. Otherwise the panel sits underneath it the moment the bar reveals.

## Animation

`Animation/ThemedMotion.cs`. Animations are built in code, not XAML.

This is not a stylistic choice. A WPF `Storyboard` is a `Freezable`, and once the template
containing it is sealed the storyboard is frozen — at which point its `Duration` can no longer be a
`DynamicResource`. A theme that cannot change how fast things move is not much of a theme, so
animations are constructed where the current values can be read.

The tier's `maxConcurrentAnimations` is enforced here too. Past the budget, animations apply
instantly rather than queueing: a late animation looks worse than no animation, and the point of
the cap is that the interface stays calm.

`Theming/Rendering/CubicBezierEase.cs` is a real cubic Bézier easing. WPF ships nothing that can
express `cubic-bezier(0.3, 1.5, 0.4, 1)` — `CubicEase` has fixed control points and `BackEase`
overshoots on a different shape. The curve is parametric, so evaluating it means solving
`x(t) = progress` first; that is Newton–Raphson with a bisection fallback for the near-flat regions
where Newton diverges. It runs on every frame of every animation and has its properties pinned by
tests.

## The theme pipeline

```
theme.json  →  ThemeDocument  →  ThemeResolver  →  Theme  →  ThemeResourceBuilder  →  ResourceDictionary
              (all nullable)     (+ ancestry)     (complete)                          (frozen, keyed)
```

The split between `ThemeDocument` and `Theme` is what makes a six-line theme possible.
`ThemeDocument` is what a person writes and every field is nullable; `Theme` is what the renderer
consumes and every field is present. Resolution collapses the `basedOn` chain onto the compiled
defaults, so no consumer ever has to reason about a missing value.

`ThemeResourceBuilder` produces one `ResourceDictionary` of frozen brushes, effects, durations and
easings. Applying a theme swaps that dictionary at index 0 of `Application.Resources`; every window
repaints and nothing is rebuilt, because the whole UI binds with `DynamicResource`.

Two things override what a theme asks for, and both win: the user's own "disable animations"
setting, and the Windows animation accessibility setting. That override is applied by **rewriting
the theme** before it is rendered, rather than by special-casing each animation site — which means
every consumer of `Fate.Duration.*` gets zero-length durations automatically and no animation can
be forgotten.

`ThemeDefaults.cs` is the only file in the codebase permitted to contain a colour literal. It
mirrors the role of `src/primitives.ts` in the brand repository. A test holds it in agreement with
the shipped `themes/fate.json`, because the two exist for different reasons and would otherwise
drift.

Hot reload is a `FileSystemWatcher` on the user's themes folder, debounced — a single save from an
editor produces three or four filesystem events.

## The Home Assistant client

`HaClient` is a supervised connection. Call `Start()` once; it owns the rest.

The receive pump, the router and the ping loop are separate tasks joined by `Task.WhenAny`, so
whichever fails first brings the connection down and the supervisor rebuilds it. Fragmented frames
are reassembled before parsing. A single malformed frame is discarded rather than killing an
otherwise working connection.

An **application-level ping** replaces the WebSocket protocol ping, because Home Assistant answers
it with a routable frame that can be timed out. A protocol ping tells you nothing about whether the
application at the other end is alive.

`HomeAssistantService` is the boundary where background-thread events become UI-thread state. No
view model ever has to think about threading.

`HaControl` maps intent onto services. Most domains answer `homeassistant.turn_on`, but the
interesting ones do not — activating a scene, running a script and firing an automation are three
different services with three different meanings. Centralising that keeps the mapping out of every
view model.

## Startup and shutdown

`App.xaml.cs` is the composition root. Services are constructed by hand rather than through a
container: there are nine of them in a strictly linear dependency order, and reading that order in
one place is more useful than the indirection.

`ShutdownMode` is `OnExplicitShutdown`. The default would quit the moment the last window closed,
which for a tray app is every time somebody closes the window.

A **named mutex** enforces one instance per user session. A second launch broadcasts a registered
window message carrying its intent — open the window, or open the panel — then exits. That is what
makes `--panel` work on a shortcut when the app is already running.

Windows are created on demand. The flyout is created once and reused, because building a WPF window
costs enough to be visible and the panel has to appear the instant it is asked for.

## Things that look wrong but are not

**`Views/Pages` are all in the visual tree at once, toggled by visibility.**
Deliberate. Swapping a `ContentControl`'s content would discard scroll position and half-finished
input every time somebody flicked between pages — worst on the settings page, where losing a
half-typed URL would be maddening. Six page view models are cheap.

**The navigation rail has no command.**
It binds `IsChecked` two-way and navigates from the selection change. Hanging navigation off a
click command would only work for a mouse; a keyboard arrow, a screen reader, or an automation
client would move the highlight without changing the page.

**The access token is never a bindable property.**
The settings view pushes `PasswordBox.Password` straight to the view model, which encrypts it
before anything persistent sees it. A bindable string would put the credential in the binding
engine and keep a managed copy alive.

**Entity icons are hand-drawn geometry rather than an icon font.**
Segoe Fluent Icons has no thermostat, no cover, no vacuum. A stroked outline also reads as engraved,
which is what the charted tier calls for; a filled pictograph would look pasted in.

**`TrackedTextBlock` renders text one glyph at a time.**
WPF has no `letter-spacing`, and the alternatives — a `Run` per character, or padding injected into
the string — break selection and accessibility. It is used for wordmarks and section labels, which
are short.

**The theming library does not know the application's assembly name.**
`ThemeResourceBuilder.EmbeddedFontBaseUri` is set by the app at startup. Hard-coding the pack URI in
the library would make it unusable from anything else, including its own tests — where the pack
scheme is not even registered.

**`Directory.Build.props` explicitly does not add `System.IO` to the global usings.**
The WindowsDesktop SDK removes it on purpose, because `System.IO.Path` collides with
`System.Windows.Shapes.Path`. WPF files declare it individually.
