# Building

## What you need

- **.NET 8 SDK** — `dotnet --version` should report 8.0.x or later.
- **Windows 10 1809 or later** to run it. The build needs Windows too: the projects target
  `net8.0-windows` and use WPF.
- **WiX 5**, only if you want the MSI:
  ```bash
  dotnet tool install --global wix
  wix extension add -g WixToolset.UI.wixext
  wix extension add -g WixToolset.Util.wixext
  ```

No Visual Studio required. The whole thing builds from the CLI.

## Day to day

```bash
dotnet build                                # everything
dotnet test                                 # 167 tests, about a second
dotnet run --project src/FateTakesYouHome   # run it
```

Running it adds a tray icon and writes to `%APPDATA%`, exactly as an installed copy would. The two
share their configuration, which is usually what you want and occasionally surprising.

## The release build

```powershell
./build/publish.ps1
```

Regenerates the icon, runs the tests, publishes self-contained for `win-x64`, and produces in
`artifacts/`:

| | |
| --- | --- |
| `publish/` | The self-contained application, ~148 MB on disk |
| `FateTakesYouHome-<version>-portable-win-x64.zip` | ~65 MB |
| `FateTakesYouHome-<version>-win-x64.msi` | ~55 MB |

```powershell
./build/publish.ps1 -Version 0.2.0      # override the version
./build/publish.ps1 -SkipInstaller      # portable only, no WiX needed
./build/publish.ps1 -Configuration Debug
```

The script **refuses to package a build whose tests fail**. That is deliberate.

## Versioning

`VersionPrefix` in `Directory.Build.props` is the single source of truth. The publish script reads
it, stamps it into the assemblies, and derives the MSI's `ProductVersion` from it.

Windows Installer compares only the first three fields of a version and rejects pre-release
suffixes, so `0.2.0-beta.1` becomes `0.2.0` in the MSI. Never ship two different builds under one
MSI version — the upgrade will not trigger.

The `UpgradeCode` GUID in `installer/wix/Package.wxs` must **never** change. It is what makes an
install an upgrade rather than a second entry in Programs and Features.

## The brand mark

The icon is generated from vector geometry, not stored as a mystery binary:

```bash
dotnet run --project build/FateIconGen
```

That rewrites `src/FateTakesYouHome/Assets/Icons/` — the multi-resolution `app.ico`, PNGs for the
README and installer, and `proof-small-sizes.png`, a magnified sheet of the tray and application
icons at 16, 20, 24 and 32 pixels.

**Look at the proof sheet after changing the mark.** Small sizes are where an icon actually fails,
and they are impossible to judge at their real size.

The geometry lives in `src/Shared/FateMark.cs`, which is **linked** into both the generator and the
application rather than referenced. A project reference would be a build cycle: the application
cannot compile until the generator has produced its icon.

Generated icons are committed, so a clean clone builds without running a tool first and a change to
the mark shows up as a reviewable diff.

## Signing

The MSI and the executable are unsigned, so SmartScreen warns on first run. To sign:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f your-certificate.pfx /p <password> `
  artifacts\FateTakesYouHome-0.1.0-win-x64.msi
```

Sign the executable inside `artifacts/publish/` **before** the packaging step, or the MSI will
carry an unsigned binary inside a signed container.

## Verifying a change by eye

Two helpers in `build/` exist because "does the window actually look right" is otherwise a manual
step, and manual steps are the ones that stop happening.

```powershell
# Drive the running application through UI Automation
./build/click-element.ps1 -ProcessName FateTakesYouHome -Name "Themes" -Pattern Select

# Capture a window to a PNG, including tool windows and partly obscured ones
./build/capture-window.ps1 -ProcessName FateTakesYouHome -Out shot.png
```

`click-element.ps1` finds elements by accessible name, which is a check worth having on its own: an
element it cannot find is an element a screen reader cannot find either.

`capture-window.ps1` enumerates top-level windows rather than using `Process.MainWindowHandle`,
because a tool window — which the tray panel is — is excluded from that by design.

## Layout notes

- `Directory.Build.props` — shared properties and product identity.
- `Directory.Packages.props` — central package versions. Add a `PackageVersion` here and a bare
  `PackageReference` in the project.
- `global.json` — pins the SDK to 8.0 with `latestFeature` roll-forward.

`FateTakesYouHome.HomeAssistant` targets plain `net8.0` and has no Windows or WPF dependency. Keep
it that way; that is what makes the client testable and reusable.
