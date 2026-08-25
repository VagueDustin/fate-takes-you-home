<!--
  CONTRIBUTING.md is the short version of what lands here:
  https://github.com/VagueDustin/fate-takes-you-home/blob/main/CONTRIBUTING.md
-->

## What changed

<!-- One or two sentences. The commit messages carry the detail. -->

## Why this rather than the obvious alternative

<!--
  The part a reviewer cannot reconstruct from the diff. If you rejected a simpler approach, say
  which and why — that reasoning is the thing most likely to be lost, and most likely to be needed
  when somebody revisits this in a year.
-->

## Checks

- [ ] `dotnet test` passes.
- [ ] Comments explain **why**, not what. British spelling in prose and in our own identifiers.
- [ ] No colour literal outside `ThemeDefaults.cs`.
- [ ] Every interactive element added has an `AutomationProperties.Name`, is reachable by
      keyboard, and shows a focus state.
- [ ] Any new animation honours reduced motion.
- [ ] Nothing in the diff came from a live Home Assistant instance — no real entity or room names,
      no server address, no screenshot of a real house. Captures come from the fake server in
      `tests/`.

## Screenshots

<!--
  Required for anything visual — before and after if you changed something that existed.
  Against fabricated data only.
-->
