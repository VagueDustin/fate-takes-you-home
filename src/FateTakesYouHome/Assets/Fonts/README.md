# Embedded typefaces

These faces are compiled into the executable as WPF resources and are addressed through a pack URI
by `ThemeResourceBuilder.ResolveFamily`. Nothing is installed into Windows, and no font is written
to disk at runtime.

| Family | Weights shipped | Used for | Licence |
| --- | --- | --- | --- |
| Inter | Regular, Medium, SemiBold, Bold | All interface text | SIL Open Font License 1.1 |
| Cinzel | Regular, Bold | The wordmark, page titles | SIL Open Font License 1.1 |
| Crimson Pro | Regular | Long-form prose, ceremonial tier only | SIL Open Font License 1.1 |

The full licence text for each family sits beside the fonts as `OFL-*.txt`. The OFL explicitly
permits embedding in a program, including a program distributed under other terms — clause 1 of
the licence. The fonts remain under the OFL; the application is AGPL-3.0-or-later. Keep the
`OFL-*.txt` files next to the fonts, since the licence requires the notice to travel with them.

## Why static weights rather than the variable fonts

Upstream ships all three of these as variable fonts, and the Google Fonts repository carries only
the variable builds. WPF has no variable font support: it loads such a file at its default
instance and synthesises anything else, so `FontWeight="SemiBold"` on a variable Inter would come
out as smeared Regular. The static instances are taken from each project's own release:

* Inter — `rsms/inter` release v4.1, `extras/ttf/`
* Cinzel — `NDISCOVER/Cinzel`, `fonts/ttf/`
* Crimson Pro — `Fonthausen/CrimsonPro`, `fonts/ttf/`

## Adding a weight

Drop the `.ttf` in this folder. The project file globs `Assets\Fonts\*.ttf` as resources, so no
build change is needed — but do check the family name inside the font matches what the theme asks
for, because WPF matches on the name recorded in the font, not the file name.
