# AGENTS.md

Guidance for coding agents working in this repository. CONTRIBUTING.md, docs/BUILDING.md and
docs/ARCHITECTURE.md are the full versions.

## What this is

Fate Takes You Home is a Windows tray app (WPF, .NET 10, `net10.0-windows`) that controls a Home
Assistant instance from a flyout. It builds and runs on Windows only.

## Layout

```
src/Shared/FateMark.cs               the brand mark geometry (linked into two projects)
src/FateTakesYouHome.HomeAssistant/  WebSocket + REST client; no UI, no WPF, no Windows dependencies
src/FateTakesYouHome.Theming/        theme model, loader, validator, WPF resource renderer
src/FateTakesYouHome/                the application (Interop, Services, ViewModels, Views, ...)
tests/FateTakesYouHome.Tests/        xUnit
themes/                              shipped themes + theme.schema.json
build/                               icon generator (FateIconGen), publish/release/bump scripts
installer/wix/                       the MSI (WiX 5)
```

Dependencies run one way: `App -> Theming`, `App -> HomeAssistant`; the two libraries never
reference each other. Package versions are central in `Directory.Packages.props`; product version
and shared build settings in `Directory.Build.props`.

## Commands

.NET 10 SDK (`global.json` pins 10.0.100 with `latestFeature` roll-forward).

```bash
dotnet restore
dotnet build                                   # everything
dotnet test                                    # xUnit suite, a few seconds; includes theme validation
dotnet run --project src/FateTakesYouHome      # run it (writes to %APPDATA% like an installed copy)
dotnet run --project build/FateIconGen         # regenerate src/FateTakesYouHome/Assets/Icons/
```

```powershell
./build/publish.ps1 -SkipInstaller             # self-contained win-x64 publish + portable zip
./build/publish.ps1                            # also the MSI (needs WiX 5.0.2 + pinned extensions)
./build/release.ps1 -Plan                      # what CI runs to check a version bump is releasable
```

CI (`.github/workflows/build.yml`) runs restore, `dotnet build --configuration Release`,
`dotnet test`, and fails if regenerating the icon changes `Assets/Icons`. Run `dotnet test` before
calling a change done.

## Conventions

- File-scoped namespaces, four-space indent, CRLF (`.editorconfig`). Explicit types over `var` where
  the type is not obvious. Nullable is on: don't silence warnings with `!` without a reason.
- Comments explain **why**, not what. XML docs on public members; reasoning goes in `<remarks>`.
- **British spelling** in prose and in our own identifiers (`Colour`), except where a framework or
  protocol forces otherwise.
- Do not add `System.IO` to global usings (it collides with `System.Windows.Shapes.Path`).
- Tests cover logic, not XAML. Name tests after the behaviour
  (`AnAutoHiddenTaskbarStillGetsItsSpaceReserved`).
- Read "Things that look wrong but are not" in docs/ARCHITECTURE.md before "fixing" something odd.

## Design and accessibility rules

- **No colour literals outside `src/FateTakesYouHome.Theming/Loading/ThemeDefaults.cs`.** Everything
  else consumes semantic roles.
- The accent means interactive or brand, never status; status uses the `status*` roles.
- Every interactive element has an `AutomationProperties.Name`, is keyboard reachable and shows a
  focus state. Bind state rather than driving behaviour from click events.
- Every animation honours reduced motion. Body text must clear WCAG AA (the validator enforces it).

## Don't

- Commit anything from a live Home Assistant instance: real entity or room names, server addresses,
  tokens, screenshots of a real house. Use the fake server in `tests/`.
- Bump `VersionPrefix` unless asked: a commit on `main` with an unreleased version **is** a release
  (CI tags and publishes it). Releases use `./build/bump-version.ps1`, which also moves the
  `CHANGELOG.md` `[Unreleased]` section on.
- Change the `UpgradeCode` GUID in `installer/wix/Package.wxs`, ever.
- Unpin the WiX extensions from the toolset version.

## Commits and PRs

Present-tense imperative subjects ("Fix the flyout margin on an auto-hidden taskbar"), one concern
per commit. The PR template asks what changed and why the obvious alternative was not it;
screenshots (fabricated data only) for anything visual.
