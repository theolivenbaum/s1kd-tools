# PDF comparison report

- **this rendering** — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/improved.pdf` — 3 page(s), 638 words
- **reference** — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/reference.pdf` — 3 page(s), 644 words

## Parity score — 81.5 / 100

| component | weight | agreement | points |
|---|---:|---:|---:|
| page count | 20 | 100.0 % | 20.0 |
| text (document-wide, pagination-blind) | 30 | 99.1 % | 29.7 |
| text (per page) | 10 | 99.2 % | 9.9 |
| ink quantity | 10 | 87.2 % | 8.7 |
| ink placement (IoU) | 30 | 43.8 % | 13.1 |

```
parity=81.5 pages=3/3 words=638/644 text=0.991 pagetext=0.992 ink=0.872 place=0.438 firstdiff=1
```

Track the components, not just the total. They fail in a fixed order: text agreement has to reach 1.0 before page count can, and page count before ink placement means anything — a page compared against the wrong page scores nonsense.

## Document metrics

| metric | this rendering | reference | delta |
|---|---:|---:|---:|
| pages | 3 | 3 | — |
| words | 638 | 644 | -6 |
| words per page (mean) | 212.7 | 214.7 | -2.0 |
| text lines | 66 | 81 | -15 |
| ink coverage per page (mean %) | 6.96 | 7.81 | -0.85 |
| differing pixels per page (mean %) | 7.53 | — | — |
| ink placement IoU (mean) | 0.438 | 1.000 | -0.562 |
| clustered difference regions | 46 | 0 | — |
| paper | A4 | A4 | — |
| body style | Liberation Serif 10.0pt | Liberation Serif 10.0pt | — |
| margins L/R/T/B (pt) | 70.9/51.0/36.2/54.3 | 70.9/51.0/36.2/54.3 | — |
| leading (pt) | 12.5 | 12.5 | — |

First page that differs: **1**.

## Per-page metrics

`ink%` is the share of the page carrying ink; `IoU` is how much of the combined ink lands in the same place on both sides; `diff%` is the share of pixels that differ; `shift` is the best-fit vertical displacement of the whole page.

| page | words | ref words | text | ink% | ref ink% | ink ratio | IoU | diff% | shift | regions | verdict |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 233 | 235 | 99 % | 7.80 | 8.36 | 0.933 | 0.421 | 9.15 | -1.2pt | 25 | REFLOW CASCADE |
| 2 | 275 | 279 | 99 % | 8.88 | 9.28 | 0.958 | 0.552 | 8.14 | 0.0pt | 8 | CONTENT DIFFERS |
| 3 | 130 | 130 | 100 % | 4.19 | 5.78 | 0.724 | 0.340 | 5.31 | 0.0pt | 4 | REWRAPPED |

## Style differences

Measured off the ink of both documents, stated as the property a stylesheet sets. These are document-wide, so one fix here usually removes many page findings.

| | property | this rendering | reference | delta | set in |
|---|---|---|---|---|---|
| • | `graphics.fills` | 0 | 5 | -5 | @background-color on fo:block / fo:table-cell |
| • | `graphics.rules` | 6 | 61 | -55 | fo:block/@border-*, fo:leader, or table borders |
| · | `text.indent-stops` | 71pt, 88pt, 406pt, 523pt | 71pt, 88pt, 91pt, 94pt, 111pt, 247pt, 346pt, 396pt | absent here: 91pt, 94pt, 111pt, 247pt, 346pt, 396pt; only here: 406pt, 523pt | fo:block/@start-indent, @text-indent, or list-block label separation |

‼ structural · • significant · · minor

## Page 1 — first divergence, in detail

**REFLOW CASCADE — the content is present but the whole page sits 1.2pt higher. One upstream measurement (a margin, a leading, a space-before) moved everything.**

- page box — this rendering 595.3x841.9pt, reference 595.3x841.9pt
- whole-page vertical shift — -1.2pt (horizontal 0.0pt)
- lines — 9 unchanged, 12 moved, 0 restyled, 0 retexted, 2 missing, 0 extra
- images — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/improved-images/page-001-diff.png` (reference faded, differing regions boxed in red), plus `-actual.png` and `-reference.png`

### Where the ink differs

Differing pixels, dilated and grouped into connected regions. Regions where one side has ink the other does not come first, then the largest. Boxes are in points from the top-left of the **reference** page.

*The whole page is displaced by -1.0pt, and that displacement was compensated for before clustering — otherwise every line on the page would contribute a sliver region saying the same thing. What follows is what differs **beyond** the shift. The metrics above are measured unaligned, so the shift itself still counts against them.*

*34 regions were found; the 25 listed here are the ones that carry the most, and the rest are smaller. This is a cap, not the whole picture.*

| # | where | box (x, y, w×h pt) | % page | ink here (ours → ref) | reading |
|---:|---|---|---:|---|---|
| 1 | middle-centre | 69.0, 332.0, 473.5×27.5 | 2.02 | 0.091 → 0.167 | ink missing from ours — glyphs, a graphic or a fill |
| 2 | middle-centre | 109.0, 379.5, 433.0×9.5 | 0.76 | 0.000 → 0.235 | ink missing from ours — glyphs, a graphic or a fill |
| 3 | top-centre | 69.0, 97.5, 477.0×4.5 | 0.43 | 0.000 → 0.265 | a rule or border the reference draws and we do not |
| 4 | middle-left | 109.0, 390.5, 66.0×9.5 | 0.12 | 0.000 → 0.260 | ink missing from ours — glyphs, a graphic or a fill |
| 5 | top-left | 69.0, 76.5, 81.5×4.0 | 0.07 | 0.152 → 0.260 | ink missing from ours — glyphs, a graphic or a fill |
| 6 | top-left | 69.0, 89.0, 81.5×4.0 | 0.07 | 0.277 → 0.170 | ink we draw that the reference does not |
| 7 | middle-left | 109.0, 369.0, 29.0×9.0 | 0.05 | 0.000 → 0.262 | ink missing from ours — glyphs, a graphic or a fill |
| 8 | top-centre | 204.0, 79.0, 55.5×4.0 | 0.04 | 0.165 → 0.270 | ink missing from ours — glyphs, a graphic or a fill |
| 9 | top-centre | 204.0, 89.0, 55.5×4.0 | 0.04 | 0.272 → 0.166 | ink we draw that the reference does not |
| 10 | top-left | 151.5, 80.5, 51.5×4.0 | 0.04 | 0.152 → 0.257 | ink missing from ours — glyphs, a graphic or a fill |
| 11 | top-left | 151.5, 89.0, 51.5×4.0 | 0.04 | 0.270 → 0.165 | ink we draw that the reference does not |
| 12 | middle-centre | 69.0, 261.5, 477.0×51.5 | 4.90 | 0.166 → 0.181 | the same ink, displaced or reshaped |
| 13 | middle-centre | 69.0, 315.0, 445.5×15.0 | 1.20 | 0.145 → 0.163 | the same ink, displaced or reshaped |
| 14 | top-centre | 69.5, 156.5, 475.0×6.0 | 0.33 | 0.265 → 0.251 | the same ink, displaced or reshaped |
| 15 | top-centre | 69.5, 181.5, 467.0×6.0 | 0.33 | 0.252 → 0.238 | the same ink, displaced or reshaped |
| 16 | top-centre | 69.5, 212.5, 465.0×6.0 | 0.33 | 0.270 → 0.256 | the same ink, displaced or reshaped |
| 17 | top-centre | 69.5, 225.0, 464.5×6.0 | 0.33 | 0.275 → 0.262 | the same ink, displaced or reshaped |
| 18 | top-centre | 69.5, 169.0, 457.0×6.0 | 0.32 | 0.265 → 0.252 | the same ink, displaced or reshaped |
| 19 | top-centre | 69.0, 45.0, 477.0×5.0 | 0.48 | 0.139 → 0.139 | the same ink, displaced or reshaped |
| 20 | bottom-centre | 69.0, 775.0, 477.0×5.0 | 0.48 | 0.099 → 0.099 | the same ink, displaced or reshaped |
| 21 | top-centre | 69.5, 237.5, 335.0×6.0 | 0.24 | 0.265 → 0.252 | the same ink, displaced or reshaped |
| 22 | top-centre | 69.5, 163.5, 475.0×3.5 | 0.33 | 0.191 → 0.214 | the same ink, displaced or reshaped |
| 23 | top-centre | 69.5, 188.5, 467.0×3.5 | 0.33 | 0.186 → 0.209 | the same ink, displaced or reshaped |
| 24 | top-centre | 69.5, 219.5, 465.0×3.5 | 0.32 | 0.190 → 0.213 | the same ink, displaced or reshaped |
| 25 | top-centre | 69.5, 232.0, 465.0×3.5 | 0.32 | 0.190 → 0.214 | the same ink, displaced or reshaped |

Region 1 (middle-centre) contains:

- reference: `Reservoir pressurization is taken from engine bleed air through a ded…` / `shut down, the reservoirs are pressurized by the hand pump on the gro…`
- ours: `All pressures quoted in this data module are gauge pressures measured…` / `location is stated.`

Region 2 (middle-centre) contains:

- reference: `All pressures quoted in this data module are gauge pressures measured…`
- ours: *(nothing)*

Region 4 (middle-left) contains:

- reference: `location is stated.`
- ours: *(nothing)*

Region 7 (middle-left) contains:

- reference: `NOTE`
- ours: *(nothing)*

### What changed, line by line

`Δx`/`Δy` are ours minus the reference, in points. `resid` is `Δy` with the whole-page shift removed — the line where `resid` first jumps is where the cascade started, and everything below it is a consequence.

| change | ref y | ref x | Δx | Δy | resid | style change | text |
|---|---:|---:|---:|---:|---:|---|---|
| moved | 41.5 | 70.9 | 0.0 | 0.0 | +1.2 | — | `AIRCRAFT MAINTENANCE MANUAL` |
| moved | 41.5 | 396.3 | +9.3 | 0.0 | +1.2 | — | `DMC-S1KDPDF-A-00-00-00-00A-040AD` |
| moved | 90.6 | 70.9 | 0.0 | 0.0 | +1.2 | — | `Hydraulic power system` |
| **missing** | 277.4 | 93.7 | — | — | — | — | `WARNING` |
| moved | 291.3 | 93.7 | -5.8 | -22.8 | -21.6 | — | `Do not disconnect any hydraulic line before the circuit has…` |
| moved | 302.3 | 93.7 | -5.8 | -22.8 | -21.6 | — | `Residual pressure in an accumulator can be sufficient to ca…` |
| moved | 328.2 | 70.9 | 0.0 | -30.6 | -29.4 | — | `Nominal system pressure is 3000 psi. The relief valve in ea…` |
| moved | 340.7 | 70.9 | 0.0 | -30.6 | -29.4 | — | `Reservoir pressurization is taken from engine bleed air thr…` |
| moved | 353.2 | 70.9 | 0.0 | -30.6 | -29.4 | — | `shut down, the reservoirs are pressurized by the hand pump …` |
| **missing** | 376.5 | 110.9 | — | — | — | — | `NOTE` |
| moved | 387.4 | 110.9 | -23.0 | -41.4 | -40.2 | — | `All pressures quoted in this data module are gauge pressure…` |
| moved | 398.4 | 110.9 | -23.0 | -41.4 | -40.2 | — | `location is stated.` |
| moved | 787.6 | 70.9 | 0.0 | 0.0 | +1.2 | — | `Issue 001 — 2026-03-17` |
| moved | 787.6 | 522.7 | 0.0 | 0.0 | +1.2 | — | `Page 1` |

Lines the reference draws and this rendering never emits, with the style they are set in:

- `WARNING` — Liberation Serif,Bold 9.0pt bold #000000, at x=93.7 y=277.4
- `NOTE` — Liberation Serif,Bold 9.0pt bold #000000, at x=110.9 y=376.5

### Structure of the reference

595.3×841.9pt, 23 line(s), 9 graphic mark(s). Positions are points from the top-left; `y` is the baseline for text and the top edge for graphics.

```
y=   41.5 x=  70.9 w= 143.9    8.0pt Liberation Serif  "AIRCRAFT MAINTENANCE MANUAL"
y=   41.5 x= 396.3 w= 148.0    8.0pt Liberation Serif  "DMC-S1KDPDF-A-00-00-00-00A-040AD"
y=   46.6 x=  70.9 w= 473.4  rule h=0.7pt #000000
y=   90.6 x=  70.9 w= 186.8   18.0pt Liberation Serif,Bold bold  "Hydraulic power system"
y=   99.3 x=  70.9 w= 473.4  rule h=1.2pt #000000
y=  115.7 x=  70.9 w=  96.2   12.0pt Liberation Serif,Italic italic  "General description"
y=  146.0 x=  70.9 w=  62.1   14.0pt Liberation Serif,Bold bold  "1. General"
y=  165.3 x=  70.9 w= 472.2   10.0pt Liberation Serif  "The hydraulic power system supplies pressurized fluid to the flight c…"
y=  177.8 x=  70.9 w= 453.9   10.0pt Liberation Serif  "the wheel brakes and the nose wheel steering unit. The system is arra…"
y=  190.3 x=  70.9 w= 464.2   10.0pt Liberation Serif  "throughout this publication as the green circuit and the yellow circu…"
y=  202.8 x=  70.9 w= 197.5   10.0pt Liberation Serif  "hydraulic power from any flight-critical function."
y=  221.3 x=  70.9 w= 461.9   10.0pt Liberation Serif  "Each circuit has its own reservoir, engine-driven pump, filter assemb…"
y=  233.8 x=  70.9 w= 462.0   10.0pt Liberation Serif  "path in normal operation. A power transfer unit couples the two circu…"
y=  246.3 x=  70.9 w= 332.1   10.0pt Liberation Serif  "that a circuit whose pump has failed can still be pressurized from th…"
y=  263.2 x=  70.9 w= 473.4  fill h=47.4pt #e8e8e8
y=  263.2 x=  70.9 w= 473.4  rule h=0.8pt #000000
y=  263.2 x=  70.9 w=   0.8  rule h=47.4pt #000000
y=  263.2 x= 543.5 w=   0.8  rule h=47.4pt #000000
y=  277.4 x=  93.7 w=  49.1    9.0pt Liberation Serif,Bold bold  "WARNING"
y=  291.3 x=  93.7 w= 433.5    9.0pt Liberation Serif  "Do not disconnect any hydraulic line before the circuit has been depr…"
y=  302.3 x=  93.7 w= 249.4    9.0pt Liberation Serif  "Residual pressure in an accumulator can be sufficient to cause injury."
y=  309.8 x=  70.9 w= 473.4  rule h=0.8pt #000000
y=  328.2 x=  70.9 w= 441.7   10.0pt Liberation Serif  "Nominal system pressure is 3000 psi. The relief valve in each circuit…"
y=  340.7 x=  70.9 w= 470.0   10.0pt Liberation Serif  "Reservoir pressurization is taken from engine bleed air through a ded…"
y=  353.2 x=  70.9 w= 353.0   10.0pt Liberation Serif  "shut down, the reservoirs are pressurized by the hand pump on the gro…"
y=  368.1 x=  70.9 w=   2.0  rule h=32.8pt #000000
y=  376.5 x= 110.9 w=  25.5    9.0pt Liberation Serif,Bold bold  "NOTE"
y=  387.4 x= 110.9 w= 429.3    9.0pt Liberation Serif  "All pressures quoted in this data module are gauge pressures measured…"
y=  398.4 x= 110.9 w=  62.7    9.0pt Liberation Serif  "location is stated."
y=  776.7 x=  70.9 w= 473.4  rule h=0.4pt #000000
y=  787.6 x=  70.9 w=  79.8    8.0pt Liberation Serif  "Issue 001 — 2026-03-17"
y=  787.6 x= 522.7 w=  21.5    8.0pt Liberation Serif  "Page 1"
```

### Structure of this rendering

595.3×841.9pt, 21 line(s), 2 graphic mark(s). Positions are points from the top-left; `y` is the baseline for text and the top edge for graphics.

```
y=   41.5 x=  70.9 w= 137.2    8.0pt Liberation Serif  "AIRCRAFT MAINTENANCE MANUAL"
y=   41.5 x= 405.6 w= 138.6    8.0pt Liberation Serif  "DMC-S1KDPDF-A-00-00-00-00A-040AD"
y=   46.6 x=  70.9 w= 473.4  rule h=0.7pt #000000
y=   90.6 x=  70.9 w= 186.8   18.0pt Liberation Serif,Bold bold  "Hydraulic power system"
y=  114.5 x=  70.9 w=  96.2   12.0pt Liberation Serif,Italic italic  "General description"
y=  144.8 x=  70.9 w=  62.1   14.0pt Liberation Serif,Bold bold  "1. General"
y=  164.1 x=  70.9 w= 472.2   10.0pt Liberation Serif  "The hydraulic power system supplies pressurized fluid to the flight c…"
y=  176.6 x=  70.9 w= 453.9   10.0pt Liberation Serif  "the wheel brakes and the nose wheel steering unit. The system is arra…"
y=  189.1 x=  70.9 w= 464.2   10.0pt Liberation Serif  "throughout this publication as the green circuit and the yellow circu…"
y=  201.6 x=  70.9 w= 197.5   10.0pt Liberation Serif  "hydraulic power from any flight-critical function."
y=  220.1 x=  70.9 w= 461.9   10.0pt Liberation Serif  "Each circuit has its own reservoir, engine-driven pump, filter assemb…"
y=  232.6 x=  70.9 w= 462.0   10.0pt Liberation Serif  "path in normal operation. A power transfer unit couples the two circu…"
y=  245.1 x=  70.9 w= 332.1   10.0pt Liberation Serif  "that a circuit whose pump has failed can still be pressurized from th…"
y=  268.5 x=  87.9 w= 433.5    9.0pt Liberation Serif  "Do not disconnect any hydraulic line before the circuit has been depr…"
y=  279.5 x=  87.9 w= 249.4    9.0pt Liberation Serif  "Residual pressure in an accumulator can be sufficient to cause injury."
y=  297.6 x=  70.9 w= 441.7   10.0pt Liberation Serif  "Nominal system pressure is 3000 psi. The relief valve in each circuit…"
y=  310.1 x=  70.9 w= 470.0   10.0pt Liberation Serif  "Reservoir pressurization is taken from engine bleed air through a ded…"
y=  322.6 x=  70.9 w= 353.0   10.0pt Liberation Serif  "shut down, the reservoirs are pressurized by the hand pump on the gro…"
y=  346.0 x=  87.9 w= 429.3    9.0pt Liberation Serif  "All pressures quoted in this data module are gauge pressures measured…"
y=  357.0 x=  87.9 w=  62.7    9.0pt Liberation Serif  "location is stated."
y=  776.7 x=  70.9 w= 473.4  rule h=0.4pt #000000
y=  787.6 x=  70.9 w=  79.8    8.0pt Liberation Serif  "Issue 001 — 2026-03-17"
y=  787.6 x= 522.7 w=  21.5    8.0pt Liberation Serif  "Page 1"
```

> 2 further page(s) differ. They are counted in every metric above but not taken apart here: differences cascade, and the page-1-of-them teardown is almost always the cause of the rest. Pass `--all-pages` once the page above is clean.

## What to change next

In the order they should be tackled — each one can change everything below it, so re-run the comparison after every change rather than batching them.

1. Emit the 2 missing line(s) on page 1, starting with "WARNING" (Liberation Serif,Bold 9.0pt bold #000000).
2. The whole page sits -1.2pt from where the reference puts it. Find the first line whose `resid` is non-zero — that is where the displacement is introduced.
3. The reference draws 1 rule/border(s) this rendering does not: top-centre at y=98pt. Look for `@border-*`, `fo:leader` or `@background-color` in the reference's styling.

