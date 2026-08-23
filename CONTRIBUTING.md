# Contributing

Thanks for looking. This is a small project with strong opinions, most of which are written down —
so the fastest way to make a change that lands is to read the reasoning first.

## Before you start

- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** explains how the pieces fit and, more usefully,
  ends with a section called *Things that look wrong but are not*. Several of the odder decisions
  here are deliberate and already have a paragraph explaining why.
- For anything visual, the design language comes from the
  [`vaguedustin-brand`](https://github.com/VagueDustin/vaguedustin-brand) repository. See
  [Design rules](#design-rules).

For a change of any size, open an issue first. It is easier to agree on an approach than to unpick
a finished branch.

## Building

```bash
dotnet build
dotnet test
dotnet run --project src/FateTakesYouHome
```

Full detail in [docs/BUILDING.md](docs/BUILDING.md).

## Code conventions

`.editorconfig` covers the mechanical parts and your editor should apply them. The rest:

- **File-scoped namespaces**, four-space indent, CRLF.
- **Explicit types** over `var` where the type is not obvious from the right-hand side.
- **Nullable reference types are on.** Do not silence a warning with `!` unless you can say why the
  value cannot be null; prefer restructuring so the compiler can see it.
- **Comments explain why, not what.** A comment restating the code is worse than none. A comment
  explaining why the obvious approach was rejected is worth a paragraph.
- **XML docs on public members**, and on anything non-obvious regardless of visibility. `<remarks>`
  is where the reasoning goes.
- **British spelling** in prose and in our own identifiers (`Colour`), except where a framework or
  protocol forces otherwise — `System.Windows.Media.Color`, `color_temp`.

## Design rules

These are the ones that come up:

- **No colour literals outside `ThemeDefaults.cs`.** Everything else consumes semantic roles.
  `ThemeDefaults.cs` is the analogue of the brand repo's `primitives.ts` and is the only legal home
  for a hex in this codebase.
- **The accent means interactive or brand.** Never status. Status uses the `status*` roles, and
  "live" is red — industry convention, not a branding decision.
- **Stay in one ornament tier.** FATE is *charted*. Ceremonial devices in a utility build read as
  noise; utility restraint in a ceremonial build reads as unfinished.
- **Honour reduced motion.** Every animation here is decorative, so there is never a reason not to.
- **Contrast is checked.** Body text must clear WCAG AA against the surface it sits on. The
  validator enforces it, and there is a test named after the one time the house style got it wrong.

`dotnet test` runs the validator against every shipped theme.

## Accessibility

Not optional, and cheap if you do it as you go:

- Every interactive element gets an `AutomationProperties.Name`.
- Everything reachable by mouse must be reachable by keyboard, and must show a focus state.
- **Do not drive behaviour from a click event when the underlying state change would do.** The
  navigation rail is the worked example: it binds selection rather than handling clicks, so
  keyboard, screen reader and automation all navigate. It did not always, and that was a bug.

`build/click-element.ps1` finds elements by accessible name. If it cannot find your control,
neither can a screen reader.

## Tests

`dotnet test` runs in a few seconds. Add tests for logic; do not add tests for XAML.

What has actually caught bugs here:

- Anything with arithmetic — placement, easing, contrast, colour parsing.
- Anything that parses a file somebody might hand-edit.
- Anything with a rule that is easy to state and easy to break silently, like "the compiled
  defaults and the shipped JSON must agree".

A test name should say what the behaviour is, not what the method is called:
`AnAutoHiddenTaskbarStillGetsItsSpaceReserved`, not `TestCompute3`.

## Commits and pull requests

- Present tense, imperative: *Fix the flyout margin on an auto-hidden taskbar*.
- One concern per commit where you can manage it.
- In the pull request, say what changed and **why the obvious alternative was not it**. That is the
  part a reviewer cannot reconstruct.
- Screenshots for anything visual — before and after, if you are changing something that existed.

## Licence

By contributing you agree your work is released under the
[GNU Affero General Public License v3 or later](LICENSE), the same terms as the project.
