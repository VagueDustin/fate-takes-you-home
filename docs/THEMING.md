# Theming

Everything you can see in Fate Takes You Home is a value in a theme file. This document is the
reference for those values.

- [The idea](#the-idea)
- [Where themes live](#where-themes-live)
- [Writing one](#writing-one)
- [Reference](#reference)
  - [Identity](#identity)
  - [Ornament tiers](#ornament-tiers)
  - [colors](#colors)
  - [typography](#typography)
  - [shape](#shape)
  - [motion](#motion)
  - [ornament](#ornament)
  - [buttons](#buttons)
  - [backdrop](#backdrop)
- [Colour notation](#colour-notation)
- [The validator](#the-validator)
- [The editor](#the-editor)
- [Recipes](#recipes)
- [Limits](#limits)

---

## The idea

**A theme is a patch, not a document.** It declares what it changes and inherits everything else.
That is the single most important thing to understand about the format, and it is why the smallest
useful theme is six lines rather than three hundred:

```json
{
  "id": "brass",
  "name": "Brass",
  "basedOn": "fate",
  "colors": { "accentDefault": "#B08D57" }
}
```

Inheritance runs `built-in defaults → basedOn ancestry → your file`, nearest value wins. A theme
with no `basedOn` still inherits the compiled FATE baseline, so nothing is ever undefined.

Two consequences worth knowing:

- **Retuning a parent flows through.** If FATE's surfaces are adjusted in a later release, `brass`
  above picks that up and keeps its own accent. A theme that copied every value would not.
- **`Duplicate` in the editor writes a patch, not a copy.** It creates a file whose only content is
  `basedOn` plus whatever you then change.

## Where themes live

```
%APPDATA%\VagueDustin Enterprises\Fate Takes You Home\Themes\
```

Drop a `.json` file there and it appears in the picker within about a second. Edit one while the
app is running and the interface repaints as you save — no restart, no reload button.

The themes that ship with the app live in `Themes\` inside the installation directory and are
read-only. A file in your own folder with the same `id` **replaces** the built-in one entirely;
delete your file to get the original back.

`theme.schema.json` is copied into your themes folder on first run. Point your editor at it and
you get completion and validation as you type:

```json
{ "$schema": "./theme.schema.json", "id": "brass" }
```

## Writing one

The quickest route is **Themes → Duplicate**, which gives you an editable patch of whatever is
selected and opens the editor on it. Everything the editor does, you can also do by hand.

Ids must be lower-case words joined by single hyphens: `midnight-brass`, not `Midnight Brass`.

---

## Reference

### Identity

| Field | Type | Notes |
| --- | --- | --- |
| `id` | string | **Required.** Kebab-case, unique. Also the file name by convention. |
| `name` | string | Shown in the picker. Falls back to the id. |
| `author` | string | |
| `version` | string | |
| `description` | string | One line, shown under the name in the picker. |
| `homepage` | string | |
| `basedOn` | string | Id of the theme to inherit from. Cycles are detected and broken; chains are followed up to 16 deep. |
| `appearance` | `dark` \| `light` | Drives the Windows title bar and caption buttons. **Must match the actual lightness of `surfaceBase`** or the window chrome will not match the content. |
| `tier` | `ceremonial` \| `charted` \| `utility` | See below. |

### Ornament tiers

The tier is what lets one design language serve both an ornate portal and a restrained control
panel. Picking one sets the defaults for every switch in [`ornament`](#ornament); the switches then
allow deliberate exceptions.

| | Ceremonial | Charted | Utility |
| --- | :-: | :-: | :-: |
| Accent behaves as | light (glow) | engraving | material (foil) |
| Display fill | gradient | flat | flat |
| Corner brackets | ✅ | ✅ | ❌ |
| Film grain | ✅ | ✅ | ❌ |
| Ornate dividers | ✅ | ❌ | ❌ |
| Glassmorphism | ✅ | ✅ | ❌ |
| Ambient motion | ✅ | ❌ | ❌ |
| Stagger entrances | ✅ | ✅ | ❌ |
| Panel edge | gradient | hairline | hairline |
| Max concurrent animations | 5 | 3 | 1 |

FATE ships at **charted**: a precision instrument rather than a ceremony, but not bare.

### `colors`

Semantic roles, not raw values. Nothing in the application references a colour any other way.

**Surfaces** — from furthest back to furthest forward:

| Role | Used for |
| --- | --- |
| `surfaceBase` | The window background, under the depth wash |
| `surfaceRaised` | Panels sitting on the background |
| `surfaceOverlay` | Cards and rows inside a panel |
| `surfaceSunken` | Recessed things: input fields, icon wells, slider tracks |
| `surfaceHighest` | Hover states, tooltips, the topmost layer |

**Borders:**

| Role | Used for |
| --- | --- |
| `borderSubtle` | Hairlines inside a panel |
| `borderDefault` | The normal visible edge |
| `borderEmphasis` | Corner brackets, focused fields |

**Text** — pick one temperature and stay in it; never mix cool and warm foregrounds in one theme:

| Role | Used for |
| --- | --- |
| `textPrimary` | Body text |
| `textMuted` | Secondary lines, captions |
| `textFaint` | Metadata, footers, hints |
| `textInverse` | Text on top of a filled accent surface |
| `textAccent` | Text that carries the accent |

**Accent** — the accent means *interactive or brand*, and nothing else:

| Role | Used for |
| --- | --- |
| `accentDefault` | The resting accent |
| `accentHover` | Lighter, on hover |
| `accentPressed` | Darker, while pressed |
| `accentSubtle` | Translucent accent for active fills |
| `accentGlow` | The halo behind an active control |

**Status** — never the accent, or a badge starts reading as a button:

`statusLive`, `statusSuccess`, `statusWarning`, `statusDanger`, `statusInfo`.

`statusLive` is red by industry convention. Do not brand it.

**`depthWash`** — an array of radial layers painted over `surfaceBase`, back to front. A flat fill
is forbidden by the house style; this is how the depth is produced.

```json
"depthWash": [
  { "centerX": 0.5, "centerY": -0.1, "radiusX": 0.8, "radiusY": 0.5,
    "color": "rgba(212, 175, 55, 0.06)", "falloff": 1.0 }
]
```

| Field | Meaning |
| --- | --- |
| `centerX`, `centerY` | Centre as a fraction of the surface. May sit outside 0–1, which is how a wash bleeds in from off-panel. |
| `radiusX`, `radiusY` | Radii as fractions of the surface |
| `color` | Colour at the centre; it fades to fully transparent |
| `falloff` | Where the fade reaches zero, 0–1 along the radius |

The array **replaces** the inherited stack wholesale rather than merging item by item — merging two
gradient stacks of different lengths produces something that is neither. `"depthWash": []` gives a
flat fill.

### `typography`

| Field | Default | Notes |
| --- | --- | --- |
| `displayFamily` | `Cinzel` | Page titles and the wordmark |
| `bodyFamily` | `Inter` | All interface text |
| `proseFamily` | `Crimson Pro` | Long-form prose; ceremonial tier only |
| `monoFamily` | `JetBrains Mono` | Paths, entity ids, hex values |
| `trackingWordmark` | `0.14` | Letter spacing in **ems** |
| `trackingLabel` | `0.34` | Letter spacing for small-caps section labels |
| `trackingDisplay` | `0.02` | |
| `scale` | `1.0` | Multiplier on every size. Clamped 0.75–2. |
| `sizeCaption` … `sizeDisplay` | 11 / 13 / 15 / 20 / 28 | |

Inter, Cinzel and Crimson Pro are embedded in the executable; nothing is installed into Windows.
Naming any other family resolves against installed fonts, with a readable fallback if it is
missing.

Letter spacing is real, not simulated — see [Limits](#limits).

### `shape`

| Field | Default | Notes |
| --- | --- | --- |
| `radiusSm` / `radiusMd` / `radiusLg` / `radiusPill` | 6 / 10 / 16 / 999 | |
| `strokeThickness` | `1` | Sub-pixel values are legitimate on high-DPI displays |
| `flyoutRadius` | `12` | The tray panel's corners |
| `flyoutWidth` | `368` | Clamped 240–900 |
| `flyoutMaxHeight` | `620` | Where the panel starts scrolling. Clamped 200–1600. |
| `flyoutMargin` | `12` | Gap between the panel and the taskbar |
| `tileHeight` | `52` | Clamped 32–120 |

### `motion`

| Field | Default | Notes |
| --- | --- | --- |
| `enabled` | `true` | Master switch. False makes every transition instant. |
| `speedScale` | `1.0` | Multiplies **every** duration. 0.5 is twice as fast. Clamped 0.1–4. |
| `flyoutOpenMs` | `220` | |
| `flyoutCloseMs` | `140` | |
| `hoverMs` | `120` | |
| `pressMs` | `70` | |
| `pageTransitionMs` | `240` | |
| `staggerStepMs` | `24` | Delay between consecutive items appearing |
| `staggerMaxItems` | `12` | Past this, everything shares the last delay |
| `flyoutEasing` | `cubic-bezier(0.3, 1.5, 0.4, 1)` | |
| `standardEasing` | `cubic-bezier(0.2, 0.7, 0.3, 1)` | |
| `flyoutTravel` | `14` | Pixels the panel slides as it opens |
| `flyoutScaleFrom` | `0.97` | Scale it grows from. `1.0` disables the zoom. |
| `respectSystemReducedMotion` | `true` | Honour the Windows animation setting. Leave this on. |

**Easing** accepts a preset name or a literal `cubic-bezier(x1, y1, x2, y2)`.

| Preset | Curve |
| --- | --- |
| `fate`, `standard`, `ease` | `cubic-bezier(0.2, 0.7, 0.3, 1)` |
| `fate-spring`, `spring`, `overshoot` | `cubic-bezier(0.3, 1.5, 0.4, 1)` |
| `linear`, `ease-in`, `ease-out`, `ease-in-out` | the usual |

Y control points may exceed 0–1, which is what produces an overshoot. X control points are clamped
to 0–1, as CSS also requires — outside that the curve is not a function of time.

`flyoutTravel` is applied in whichever direction the taskbar is: the panel always emerges *out of*
the bar, whether that is the bottom, top, left or right of the screen.

### `ornament`

Each switch defaults to whatever the [tier](#ornament-tiers) prescribes. Set one only to make a
deliberate exception.

`cornerBrackets`, `filmGrain`, `filmGrainOpacity` (0–0.4), `glassmorphism`, `ornateDividers`,
`ambientMotion`, `staggerEntrances`, `gradientDisplayFill`, `depthWash`,
`panelEdge` (`hairline` | `gradient`), `maxConcurrentAnimations` (1–32).

`maxConcurrentAnimations` is enforced. Past the budget, animations apply instantly rather than
queueing — a late animation looks worse than none, and the point of the cap is that the interface
stays calm.

### `buttons`

| Field | Default | Notes |
| --- | --- | --- |
| `style` | `engraved` | See below |
| `radius` | *(follows `shape.radiusMd`)* | |
| `hoverLift` | `1` | Pixels the control rises on hover |
| `pressScale` | `0.985` | Clamped 0.5–1.5 |
| `activeGlow` | tier-dependent | |
| `hoverSheen` | tier-dependent | Ceremonial and charted only |
| `padding` | `10` | |

| Style | Looks like |
| --- | --- |
| `engraved` | Recessed face with a lit top edge; the accent is a stroke around it |
| `foil` | Solid accent slab that darkens as it is pressed |
| `glow` | Dark face; the accent arrives as a halo |
| `ghost` | Nothing until you approach it |
| `outline` | Hairline outline, transparent fill |
| `pill` | Fully rounded, filled |

The style is resolved into concrete brushes when the theme is applied, so one control template
serves all six.

### `backdrop`

| Field | Default | Notes |
| --- | --- | --- |
| `mode` | `composited` | See below |
| `tintOpacity` | `0.86` | Tint over a system backdrop, 0–1 |
| `flyoutOpacity` | `1.0` | The panel's overall opacity once open. Clamped 0.2–1. |
| `shadowBlur` | `34` | |
| `shadowOpacity` | `0.85` | |
| `shadowDepth` | `10` | |

| Mode | Behaviour |
| --- | --- |
| `composited` | The app paints everything. Identical on every Windows build, and the only mode with full control of the entrance animation. **The default, and the one to use unless you have a reason.** |
| `acrylic` | Windows 11 blur behind the panel. Falls back to composited where Windows refuses. |
| `mica` | Windows 11 mica. Applies to the **full window only** — see [Limits](#limits). |
| `solid` | Opaque fill. Cheapest. |

Changing the mode rebuilds the flyout window, because a WPF window's transparency is fixed once its
handle exists. You will see the panel disappear and come back; that is expected.

---

## Colour notation

| Form | Example |
| --- | --- |
| Hex, 3 or 6 digits | `#FA0`, `#D4AF37` |
| Hex with alpha, 4 or 8 digits | `#FA08`, `#D4AF3719` |
| `rgb()` / `rgba()` | `rgb(212, 175, 55)`, `rgba(212, 175, 55, 0.1)` |
| Modern space form | `rgb(212 175 55 / 0.1)` |
| Percentages | `rgb(100%, 0%, 50%)` |
| Named | `transparent`, `red` |

**Hex alpha is read in CSS order — `#RRGGBBAA`, alpha last.** WPF's own parser reads `#AARRGGBB`,
alpha first. This deliberately differs from WPF because theme authors copy values out of CSS, and
reading them in the wrong channel order would turn a 10%-opacity gold into an almost-black blue
with no error.

## The validator

Every theme is checked when it loads and again on every keystroke in the editor. Errors stop a
theme being used; warnings do not.

**Errors**

- A missing or malformed `id`
- A colour that cannot be parsed
- Self-inheritance
- `textPrimary` failing WCAG AA (4.5:1) against `surfaceBase` or `surfaceRaised`

**Warnings**

- `textMuted` or `textFaint` failing AA
- `accentDefault` below 3:1 against the base surface
- `textInverse` unreadable on a filled accent button
- A status colour nearly indistinguishable from the accent
- `appearance` disagreeing with the lightness of `surfaceBase`
- Ceremonial devices in a utility-tier theme
- `respectSystemReducedMotion` switched off

The contrast rules are not decoration. The house style has already shipped one unreadable
foreground; that specific regression has a test named after it.

## The editor

**Themes → Edit** (or **Duplicate**, for a built-in). Every change previews live against the whole
running application — this is the only honest way to judge a colour, because a swatch in a form
tells you nothing about how it reads on a panel next to everything else.

Nothing is written until **Save**. **Discard** repaints whatever is actually selected. The editor
covers the fields people change most; anything it does not expose is available by editing the file,
and the file and the editor stay in step because both go through the same document.

The editor also does **Import** and **Export**, which are just file copies — a theme is one
self-contained JSON file with no assets.

## Recipes

**Just change the accent**

```json
{ "id": "teal", "name": "Teal", "basedOn": "fate",
  "colors": { "accentDefault": "#46D6C4", "accentHover": "#6FE3D4",
              "accentPressed": "#2FA898", "textAccent": "#6FE3D4",
              "accentSubtle": "rgba(70, 214, 196, 0.12)",
              "accentGlow":   "rgba(70, 214, 196, 0.45)" } }
```

**Make everything faster**

```json
{ "id": "brisk", "name": "Brisk", "basedOn": "fate",
  "motion": { "speedScale": 0.6 } }
```

**Turn off the decoration but keep the palette**

```json
{ "id": "plain", "name": "Plain", "basedOn": "fate", "tier": "utility" }
```

**A bigger, calmer panel**

```json
{ "id": "roomy", "name": "Roomy", "basedOn": "fate",
  "shape": { "flyoutWidth": 460, "tileHeight": 64, "radiusMd": 14 },
  "motion": { "flyoutOpenMs": 300, "flyoutEasing": "fate" } }
```

**No motion at all**

```json
{ "id": "still", "name": "Still", "basedOn": "fate",
  "motion": { "enabled": false } }
```

## Limits

Worth knowing before you spend an afternoon on something that cannot work.

- **Variable fonts are not supported.** WPF loads a variable font at its default instance and
  synthesises the rest, so a variable Inter would render SemiBold as smeared Regular. The bundled
  faces are static instances. If you name your own font, use static weights.
- **`mica` does not apply to the tray panel.** The panel is a layered window so that the app can
  composite and animate it; the documented DWM backdrop has nothing to draw behind a layered
  window. `acrylic` works there through a different route. `mica` applies to the full window.
- **Letter spacing is drawn, not faked.** WPF has no `letter-spacing`, so tracked text is rendered
  glyph by glyph. It is used for wordmarks and section labels — short strings — and is not suitable
  for a paragraph.
- **Animation durations are read at the moment an animation starts.** A theme change takes effect
  on the next interaction, not mid-flight.
- **A theme cannot add controls or change layout.** It changes how things look, not what is there.
