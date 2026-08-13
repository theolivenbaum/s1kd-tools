# PDF comparison report

- **this rendering** — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/baseline.pdf` — 2 page(s), 601 words
- **reference** — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/reference.pdf` — 3 page(s), 644 words

## Parity score — 47.1 / 100

| component | weight | agreement | points |
|---|---:|---:|---:|
| page count | 20 | 66.7 % | 13.3 |
| text (document-wide, pagination-blind) | 30 | 93.2 % | 28.0 |
| text (per page) | 10 | 18.2 % | 1.8 |
| ink quantity | 10 | 21.7 % | 2.2 |
| ink placement (IoU) | 30 | 6.2 % | 1.9 |

```
parity=47.1 pages=2/3 words=601/644 text=0.932 pagetext=0.182 ink=0.217 place=0.062 firstdiff=1
```

Track the components, not just the total. They fail in a fixed order: text agreement has to reach 1.0 before page count can, and page count before ink placement means anything — a page compared against the wrong page scores nonsense.

## Document metrics

| metric | this rendering | reference | delta |
|---|---:|---:|---:|
| pages | 2 | 3 | -1 |
| words | 601 | 644 | -43 |
| words per page (mean) | 300.5 | 214.7 | +85.8 |
| text lines | 57 | 81 | -24 |
| ink coverage per page (mean %) | 13.45 | 8.82 | +4.63 |
| differing pixels per page (mean %) | 22.16 | — | — |
| ink placement IoU (mean) | 0.094 | 1.000 | -0.906 |
| clustered difference regions | 67 | 0 | — |
| paper | A4 | A4 | — |
| body style | Liberation Serif 12.0pt | Liberation Serif 10.0pt | differs |
| margins L/R/T/B (pt) | 56.7/57.5/59.6/64.5 | 70.9/51.0/36.2/54.3 | differs |
| leading (pt) | 14.4 | 12.5 | differs |

First page that differs: **1**.

## Per-page metrics

`ink%` is the share of the page carrying ink; `IoU` is how much of the combined ink lands in the same place on both sides; `diff%` is the share of pixels that differ; `shift` is the best-fit vertical displacement of the whole page.

| page | words | ref words | text | ink% | ref ink% | ink ratio | IoU | diff% | shift | regions | verdict |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 467 | 235 | 48 % | 20.87 | 8.36 | 2.498 | 0.084 | 28.05 | -34.3pt | 25 | DIFFERENT CONTENT |
| 2 | 134 | 279 | 7 % | 6.03 | 9.28 | 0.650 | 0.103 | 16.27 | -414.0pt | 25 | DIFFERENT CONTENT |
| 3 | 0 | 130 | — | — | — | — | — | — | — | — | PAGE MISSING |

## Style differences

Measured off the ink of both documents, stated as the property a stylesheet sets. These are document-wide, so one fix here usually removes many page findings.

| | property | this rendering | reference | delta | set in |
|---|---|---|---|---|---|
| ‼ | `body.font-size` | 12.0pt | 10.0pt | +2.0pt | fo:block/@font-size |
| ‼ | `page.margin-bottom` | 64.5pt (22.8mm) | 54.3pt (19.2mm) | +10.3pt (+3.6mm) | fo:simple-page-master/@margin-bottom (or region-after/@extent) |
| ‼ | `page.margin-left` | 56.7pt (20.0mm) | 70.9pt (25.0mm) | -14.2pt (-5.0mm) | fo:simple-page-master/@margin-left |
| ‼ | `page.margin-right` | 57.5pt (20.3mm) | 51.0pt (18.0mm) | +6.5pt (+2.3mm) | fo:simple-page-master/@margin-right |
| ‼ | `page.margin-top` | 59.6pt (21.0mm) | 36.2pt (12.8mm) | +23.4pt (+8.3mm) | fo:simple-page-master/@margin-top (or region-before/@extent) |
| ‼ | `page.running-footer` | (none) | Issue # — #-#-# ⏐ Page # | — | fo:static-content flow-name="xsl-region-after" |
| ‼ | `page.running-header` | (none) | AIRCRAFT MAINTENANCE MANUAL ⏐ DMC-S#KDPDF-A-#-#-#-#A-#AD | — | fo:static-content flow-name="xsl-region-before" |
| • | `body.line-height` | 14.4pt | 12.5pt | +1.9pt | fo:block/@line-height |
| • | `body.line-height (relative)` | 1.20× font size | 1.25× font size | -0.05× | fo:block/@line-height as a multiplier |
| • | `graphics.fills` | 0 | 5 | -5 | @background-color on fo:block / fo:table-cell |
| • | `graphics.rules` | 0 | 61 | -61 | fo:block/@border-*, fo:leader, or table borders |
| • | `text.largest` | Liberation Serif 12.0pt — e.g. "cavitation threshold of the engine-driven pump at all attit…" | Liberation Serif,Bold 18.0pt bold — e.g. "Hydraulic power system" | -6.0pt | fo:block/@font-size, @font-weight, @color for this role |
| • | `text.size-rank-2` | (absent) | Liberation Serif,Bold 14.0pt bold | — | a text style the reference uses and this rendering never produces |
| • | `text.size-rank-3` | (absent) | Liberation Serif,Italic 12.0pt italic | — | a text style the reference uses and this rendering never produces |
| • | `text.size-rank-4` | (absent) | Liberation Serif,Bold 11.5pt bold | — | a text style the reference uses and this rendering never produces |
| • | `text.size-rank-5` | (absent) | Liberation Serif 10.0pt | — | a text style the reference uses and this rendering never produces |
| • | `text.size-rank-6` | (absent) | Liberation Serif 9.0pt | — | a text style the reference uses and this rendering never produces |
| · | `text.indent-stops` | 57pt | 71pt, 88pt, 91pt, 94pt, 111pt, 247pt, 346pt, 396pt | absent here: 71pt, 88pt, 91pt, 94pt, 111pt, 247pt, 346pt, 396pt; only here: 57pt | fo:block/@start-indent, @text-indent, or list-block label separation |

‼ structural · • significant · · minor

## Page 1 — first divergence, in detail

**DIFFERENT CONTENT — only 48 % of the words agree. This is probably not the same page: check pagination before reading anything below.**

- page box — this rendering 595.3x841.9pt, reference 595.3x841.9pt
- whole-page vertical shift — -34.3pt (horizontal -14.2pt)
- lines — 0 unchanged, 0 moved, 0 restyled, 15 retexted, 8 missing, 27 extra
- images — `/home/user/s1kd-tools/samples/out/pdfdiff-demo/baseline-images/page-001-diff.png` (reference faded, differing regions boxed in red), plus `-actual.png` and `-reference.png`

### Where the ink differs

Differing pixels, dilated and grouped into connected regions. Regions where one side has ink the other does not come first, then the largest. Boxes are in points from the top-left of the **reference** page.

*The whole page is displaced by -36.5pt, and that displacement was compensated for before clustering — otherwise every line on the page would contribute a sliver region saying the same thing. What follows is what differs **beyond** the shift. The metrics above are measured unaligned, so the shift itself still counts against them.*

*40 regions were found; the 25 listed here are the ones that carry the most, and the rest are smaller. This is a cap, not the whole picture.*

| # | where | box (x, y, w×h pt) | % page | ink here (ours → ref) | reading |
|---:|---|---|---:|---|---|
| 1 | bottom-centre | 55.0, 771.0, 491.0×26.0 | 1.60 | 0.165 → 0.036 | ink we draw that the reference does not |
| 2 | top-centre | 55.0, 134.5, 467.0×15.5 | 1.06 | 0.204 → 0.036 | ink we draw that the reference does not |
| 3 | middle-centre | 55.0, 344.5, 475.5×12.5 | 1.01 | 0.254 → 0.157 | ink we draw that the reference does not |
| 4 | bottom-centre | 55.0, 560.0, 484.0×12.0 | 1.11 | 0.273 → 0.000 | ink we draw that the reference does not |
| 5 | bottom-centre | 55.0, 574.5, 481.0×12.0 | 1.11 | 0.266 → 0.000 | ink we draw that the reference does not |
| 6 | bottom-centre | 55.0, 659.5, 468.0×12.0 | 1.05 | 0.257 → 0.000 | ink we draw that the reference does not |
| 7 | middle-centre | 55.0, 442.5, 484.5×11.5 | 1.06 | 0.273 → 0.000 | ink we draw that the reference does not |
| 8 | middle-centre | 55.0, 498.5, 464.0×12.0 | 1.09 | 0.273 → 0.000 | ink we draw that the reference does not |
| 9 | bottom-centre | 55.0, 696.5, 482.5×11.5 | 1.06 | 0.275 → 0.000 | ink we draw that the reference does not |
| 10 | middle-centre | 55.0, 527.5, 469.5×11.5 | 1.05 | 0.285 → 0.000 | ink we draw that the reference does not |
| 11 | bottom-centre | 55.0, 756.5, 446.5×12.0 | 1.05 | 0.283 → 0.000 | ink we draw that the reference does not |
| 12 | bottom-centre | 55.0, 631.0, 456.5×11.5 | 1.00 | 0.268 → 0.000 | ink we draw that the reference does not |
| 13 | bottom-centre | 55.0, 645.5, 449.0×11.5 | 0.98 | 0.272 → 0.000 | ink we draw that the reference does not |
| 14 | middle-centre | 55.0, 513.0, 447.5×11.5 | 0.97 | 0.263 → 0.000 | ink we draw that the reference does not |
| 15 | bottom-centre | 55.0, 804.0, 290.5×11.5 | 0.66 | 0.288 → 0.000 | ink we draw that the reference does not |
| 16 | bottom-left | 55.0, 710.5, 274.0×12.0 | 0.60 | 0.259 → 0.000 | ink we draw that the reference does not |
| 17 | top-left | 55.0, 194.0, 215.0×13.5 | 0.44 | 0.056 → 0.187 | ink missing from ours — glyphs, a graphic or a fill |
| 18 | bottom-left | 55.0, 674.0, 182.5×11.5 | 0.40 | 0.269 → 0.000 | ink we draw that the reference does not |
| 19 | top-centre | 69.0, 45.0, 477.0×4.0 | 0.38 | 0.000 → 0.174 | a rule or border the reference draws and we do not |
| 20 | bottom-left | 55.0, 589.0, 150.0×11.5 | 0.33 | 0.282 → 0.000 | ink we draw that the reference does not |
| 21 | middle-left | 55.0, 456.5, 136.0×12.0 | 0.30 | 0.263 → 0.000 | ink we draw that the reference does not |
| 22 | middle-left | 55.0, 425.0, 123.5×11.5 | 0.28 | 0.303 → 0.000 | ink we draw that the reference does not |
| 23 | top-right | 394.5, 34.5, 151.5×8.5 | 0.26 | 0.000 → 0.276 | ink missing from ours — glyphs, a graphic or a fill |
| 24 | top-left | 69.0, 76.5, 81.5×15.5 | 0.25 | 0.000 → 0.347 | ink missing from ours — glyphs, a graphic or a fill |
| 25 | bottom-left | 55.0, 613.5, 109.5×11.5 | 0.23 | 0.279 → 0.000 | ink we draw that the reference does not |

Region 1 (bottom-centre) contains:

- reference: `Issue 001 — 2026-03-17` / `Page 1`
- ours: `pressure transients caused by rapid actuator movement and provides a …` / `brake application after the loss of both pumps.`

Region 2 (top-centre) contains:

- reference: `1. General`
- ours: `The hydraulic power system supplies pressurized fluid to the flight c…`

Region 3 (middle-centre) contains:

- reference: `shut down, the reservoirs are pressurized by the hand pump on the gro…`
- ours: `on the ground, with the engines shut down, the reservoirs are pressur…`

Region 4 (bottom-centre) contains:

- reference: *(nothing)*
- ours: `A sight glass on the forward face of the reservoir shows the fluid le…`

Region 5 (bottom-centre) contains:

- reference: *(nothing)*
- ours: `, FULL and MAX . The level must be read with the aircraft on the grou…`

Region 6 (bottom-centre) contains:

- reference: *(nothing)*
- ours: `nominal value across the whole flow range. At zero demand the pump de…`

Region 7 (middle-centre) contains:

- reference: *(nothing)*
- ours: `The main components of each circuit are listed below. Part numbers ar…`

Region 8 (middle-centre) contains:

- reference: *(nothing)*
- ours: `The reservoir is a welded aluminium cylinder of 12 litre usable capac…`

### What changed, line by line

`Δx`/`Δy` are ours minus the reference, in points. `resid` is `Δy` with the whole-page shift removed — the line where `resid` first jumps is where the cascade started, and everything below it is a consequence.

| change | ref y | ref x | Δx | Δy | resid | style change | text |
|---|---:|---:|---:|---:|---:|---|---|
| **missing** | 41.5 | 70.9 | — | — | — | — | `AIRCRAFT MAINTENANCE MANUAL` |
| **missing** | 41.5 | 396.3 | — | — | — | — | `DMC-S1KDPDF-A-00-00-00-00A-040AD` |
| retexted | 90.6 | 70.9 | -14.2 | -22.6 | +11.7 | font-size 18.0pt → 12.0pt | `Hydraulic power system` → `Hydraulic power system — General description` |
| retexted | 115.7 | 70.9 | -14.2 | -21.4 | +12.9 | font-family Liberation Serif,Italic → Liberation Serif,Bold; font-weight normal → bold; font-style italic → normal | `General description` → `General` |
| **extra** | 140.5 | 56.7 | — | — | — | — | `two independent circuits, referred to throughout this publi…` |
| **missing** | 146.0 | 70.9 | — | — | — | — | `1. General` |
| retexted | 165.3 | 70.9 | -14.2 | -53.6 | -19.3 | font-size 10.0pt → 12.0pt | `The hydraulic power system supplies pressuri…` → `The hydraulic power system supplies pressuri…` |
| **extra** | 169.3 | 56.7 | — | — | — | — | `function.` |
| retexted | 177.8 | 70.9 | -14.2 | -51.7 | -17.4 | font-size 10.0pt → 12.0pt | `the wheel brakes and the nose wheel steering…` → `gear retraction jacks, the wheel brakes and …` |
| retexted | 190.3 | 70.9 | -14.2 | -35.4 | -1.1 | font-size 10.0pt → 12.0pt | `throughout this publication as the green cir…` → `circuit, so that the loss of one circuit doe…` |
| **missing** | 202.8 | 70.9 | — | — | — | — | `hydraulic power from any flight-critical function.` |
| retexted | 221.3 | 70.9 | -14.2 | -33.6 | +0.7 | font-size 10.0pt → 12.0pt | `Each circuit has its own reservoir, engine-d…` → `Each circuit has its own reservoir, engine-d…` |
| **extra** | 230.9 | 56.7 | — | — | — | — | `pressurized from the opposite side.` |
| retexted | 233.8 | 70.9 | -14.2 | -31.7 | +2.6 | font-size 10.0pt → 12.0pt | `path in normal operation. A power transfer u…` → `circuits share no fluid path in normal opera…` |
| retexted | 246.3 | 70.9 | -14.2 | -29.8 | +4.5 | font-size 10.0pt → 12.0pt | `that a circuit whose pump has failed can sti…` → `mechanically, without transferring fluid, so…` |
| **missing** | 277.4 | 93.7 | — | — | — | — | `WARNING` |
| retexted | 291.3 | 93.7 | -37.0 | -38.0 | -3.7 | font-size 9.0pt → 12.0pt | `Do not disconnect any hydraulic line before …` → `Do not disconnect any hydraulic line before …` |
| retexted | 302.3 | 93.7 | -37.0 | -34.6 | -0.3 | font-size 9.0pt → 12.0pt | `Residual pressure in an accumulator can be s…` → `has been discharged. Residual pressure in an…` |
| retexted | 328.2 | 70.9 | -14.2 | -38.1 | -3.8 | font-size 10.0pt → 12.0pt | `Nominal system pressure is 3000 psi. The rel…` → `Nominal system pressure is 3000 psi. The rel…` |
| **extra** | 333.3 | 56.7 | — | — | — | — | `ground service panel.` |
| retexted | 340.7 | 70.9 | -14.2 | -36.2 | -1.9 | font-size 10.0pt → 12.0pt | `Reservoir pressurization is taken from engin…` → `at 3200 psi. Reservoir pressurization is tak…` |
| retexted | 353.2 | 70.9 | -14.2 | -34.3 | 0.0 | font-size 10.0pt → 12.0pt | `shut down, the reservoirs are pressurized by…` → `on the ground, with the engines shut down, t…` |
| **missing** | 376.5 | 110.9 | — | — | — | — | `NOTE` |
| retexted | 387.4 | 110.9 | -54.2 | -31.7 | +2.6 | font-size 9.0pt → 12.0pt | `All pressures quoted in this data module are…` → `All pressures quoted in this data module are…` |
| retexted | 398.4 | 110.9 | -54.2 | -28.3 | +6.0 | font-size 9.0pt → 12.0pt | `location is stated.` → `transducer unless another location is stated.` |
| **extra** | 398.5 | 56.7 | — | — | — | — | `Component description` |
| **extra** | 415.9 | 56.7 | — | — | — | — | `The main components of each circuit are listed below. Part …` |
| **extra** | 430.3 | 56.7 | — | — | — | — | `data module for the system.` |
| **extra** | 454.7 | 56.7 | — | — | — | — | `Reservoir` |
| **extra** | 472.1 | 56.7 | — | — | — | — | `The reservoir is a welded aluminium cylinder of 12 litre us…` |
| **extra** | 486.5 | 56.7 | — | — | — | — | `The piston is driven by system pressure so that the return …` |
| **extra** | 500.9 | 56.7 | — | — | — | — | `cavitation threshold of the engine-driven pump at all attit…` |
| **extra** | 515.3 | 56.7 | — | — | — | — | `envelope.` |
| **extra** | 533.7 | 56.7 | — | — | — | — | `A sight glass on the forward face of the reservoir shows th…` |
| **extra** | 548.1 | 56.7 | — | — | — | — | `, FULL and MAX . The level must be read with the aircraft o…` |
| **extra** | 562.5 | 56.7 | — | — | — | — | `and the landing gear extended.` |
| **extra** | 586.9 | 56.7 | — | — | — | — | `Engine-driven pump` |
| **extra** | 604.3 | 56.7 | — | — | — | — | `The engine-driven pump is a variable displacement axial pis…` |
| **extra** | 618.7 | 56.7 | — | — | — | — | `gearbox. Displacement is controlled by a compensator that m…` |
| **extra** | 633.1 | 56.7 | — | — | — | — | `nominal value across the whole flow range. At zero demand t…` |
| **extra** | 647.5 | 56.7 | — | — | — | — | `flow falls to the case drain flow only.` |
| **extra** | 669.9 | 56.7 | — | — | — | — | `Running the pump dry for more than fifteen seconds will dam…` |
| **extra** | 684.3 | 56.7 | — | — | — | — | `before the first engine start after any component change.` |
| **extra** | 712.7 | 56.7 | — | — | — | — | `Accumulator` |
| **extra** | 730.1 | 56.7 | — | — | — | — | `Each circuit has a piston accumulator precharged with dry n…` |
| **extra** | 744.5 | 56.7 | — | — | — | — | `pressure transients caused by rapid actuator movement and p…` |
| **extra** | 758.9 | 56.7 | — | — | — | — | `brake application after the loss of both pumps.` |
| **extra** | 777.3 | 56.7 | — | — | — | — | `Green circuit accumulator, 1.5 litre, precharged to 1800 ps…` |
| **missing** | 787.6 | 70.9 | — | — | — | — | `Issue 001 — 2026-03-17` |
| **missing** | 787.6 | 522.7 | — | — | — | — | `Page 1` |

Lines the reference draws and this rendering never emits, with the style they are set in:

- `AIRCRAFT MAINTENANCE MANUAL` — Liberation Serif 8.0pt #000000, at x=70.9 y=41.5
- `DMC-S1KDPDF-A-00-00-00-00A-040AD` — Liberation Serif 8.0pt #000000, at x=396.3 y=41.5
- `1. General` — Liberation Serif,Bold 14.0pt bold #000000, at x=70.9 y=146.0
- `hydraulic power from any flight-critical function.` — Liberation Serif 10.0pt #000000, at x=70.9 y=202.8
- `WARNING` — Liberation Serif,Bold 9.0pt bold #000000, at x=93.7 y=277.4
- `NOTE` — Liberation Serif,Bold 9.0pt bold #000000, at x=110.9 y=376.5
- `Issue 001 — 2026-03-17` — Liberation Serif 8.0pt #000000, at x=70.9 y=787.6
- `Page 1` — Liberation Serif 8.0pt #000000, at x=522.7 y=787.6

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

595.3×841.9pt, 42 line(s), 0 graphic mark(s). Positions are points from the top-left; `y` is the baseline for text and the top edge for graphics.

```
y=   67.9 x=  56.7 w= 244.1   12.0pt Liberation Serif,Bold bold  "Hydraulic power system — General description"
y=   94.3 x=  56.7 w=  41.3   12.0pt Liberation Serif,Bold bold  "General"
y=  111.7 x=  56.7 w= 463.5   12.0pt Liberation Serif  "The hydraulic power system supplies pressurized fluid to the flight c…"
y=  126.1 x=  56.7 w= 470.7   12.0pt Liberation Serif  "gear retraction jacks, the wheel brakes and the nose wheel steering u…"
y=  140.5 x=  56.7 w= 474.4   12.0pt Liberation Serif  "two independent circuits, referred to throughout this publication as …"
y=  154.9 x=  56.7 w= 450.8   12.0pt Liberation Serif  "circuit, so that the loss of one circuit does not remove hydraulic po…"
y=  169.3 x=  56.7 w=  43.0   12.0pt Liberation Serif  "function."
y=  187.7 x=  56.7 w= 447.1   12.0pt Liberation Serif  "Each circuit has its own reservoir, engine-driven pump, filter assemb…"
y=  202.1 x=  56.7 w= 450.8   12.0pt Liberation Serif  "circuits share no fluid path in normal operation. A power transfer un…"
y=  216.5 x=  56.7 w= 438.8   12.0pt Liberation Serif  "mechanically, without transferring fluid, so that a circuit whose pum…"
y=  230.9 x=  56.7 w= 167.5   12.0pt Liberation Serif  "pressurized from the opposite side."
y=  253.3 x=  56.7 w= 475.4   12.0pt Liberation Serif  "Do not disconnect any hydraulic line before the circuit has been depr…"
y=  267.7 x=  56.7 w= 435.1   12.0pt Liberation Serif  "has been discharged. Residual pressure in an accumulator can be suffi…"
y=  290.1 x=  56.7 w= 471.5   12.0pt Liberation Serif  "Nominal system pressure is 3000 psi. The relief valve in each circuit…"
y=  304.5 x=  56.7 w= 468.4   12.0pt Liberation Serif  "at 3200 psi. Reservoir pressurization is taken from engine bleed air …"
y=  318.9 x=  56.7 w= 471.9   12.0pt Liberation Serif  "on the ground, with the engines shut down, the reservoirs are pressur…"
y=  333.3 x=  56.7 w= 102.9   12.0pt Liberation Serif  "ground service panel."
y=  355.7 x=  56.7 w= 447.5   12.0pt Liberation Serif  "All pressures quoted in this data module are gauge pressures measured…"
y=  370.1 x=  56.7 w= 208.4   12.0pt Liberation Serif  "transducer unless another location is stated."
y=  398.5 x=  56.7 w= 120.3   12.0pt Liberation Serif,Bold bold  "Component description"
y=  415.9 x=  56.7 w= 481.0   12.0pt Liberation Serif  "The main components of each circuit are listed below. Part numbers ar…"
y=  430.3 x=  56.7 w= 132.9   12.0pt Liberation Serif  "data module for the system."
y=  454.7 x=  56.7 w=  49.9   12.0pt Liberation Serif,Bold bold  "Reservoir"
y=  472.1 x=  56.7 w= 460.8   12.0pt Liberation Serif  "The reservoir is a welded aluminium cylinder of 12 litre usable capac…"
y=  486.5 x=  56.7 w= 444.2   12.0pt Liberation Serif  "The piston is driven by system pressure so that the return side of th…"
y=  500.9 x=  56.7 w= 466.4   12.0pt Liberation Serif  "cavitation threshold of the engine-driven pump at all attitudes and a…"
y=  515.3 x=  56.7 w=  46.3   12.0pt Liberation Serif  "envelope."
y=  533.7 x=  56.7 w= 480.5   12.0pt Liberation Serif  "A sight glass on the forward face of the reservoir shows the fluid le…"
y=  548.1 x=  56.7 w= 477.8   12.0pt Liberation Serif  ", FULL and MAX . The level must be read with the aircraft on the grou…"
y=  562.5 x=  56.7 w= 146.8   12.0pt Liberation Serif  "and the landing gear extended."
y=  586.9 x=  56.7 w= 106.3   12.0pt Liberation Serif,Bold bold  "Engine-driven pump"
y=  604.3 x=  56.7 w= 453.1   12.0pt Liberation Serif  "The engine-driven pump is a variable displacement axial piston unit m…"
y=  618.7 x=  56.7 w= 445.8   12.0pt Liberation Serif  "gearbox. Displacement is controlled by a compensator that maintains d…"
y=  633.1 x=  56.7 w= 464.8   12.0pt Liberation Serif  "nominal value across the whole flow range. At zero demand the pump de…"
y=  647.5 x=  56.7 w= 179.1   12.0pt Liberation Serif  "flow falls to the case drain flow only."
y=  669.9 x=  56.7 w= 479.2   12.0pt Liberation Serif  "Running the pump dry for more than fifteen seconds will damage the pi…"
y=  684.3 x=  56.7 w= 270.6   12.0pt Liberation Serif  "before the first engine start after any component change."
y=  712.7 x=  56.7 w=  67.3   12.0pt Liberation Serif,Bold bold  "Accumulator"
y=  730.1 x=  56.7 w= 443.1   12.0pt Liberation Serif  "Each circuit has a piston accumulator precharged with dry nitrogen. T…"
y=  744.5 x=  56.7 w= 470.8   12.0pt Liberation Serif  "pressure transients caused by rapid actuator movement and provides a …"
y=  758.9 x=  56.7 w= 222.4   12.0pt Liberation Serif  "brake application after the loss of both pumps."
y=  777.3 x=  56.7 w= 287.0   12.0pt Liberation Serif  "Green circuit accumulator, 1.5 litre, precharged to 1800 psi."
```

> 2 further page(s) differ. They are counted in every metric above but not taken apart here: differences cascade, and the page-1-of-them teardown is almost always the cause of the rest. Pass `--all-pages` once the page above is clean.

## What to change next

In the order they should be tackled — each one can change everything below it, so re-run the comparison after every change rather than batching them.

1. Set `body.font-size` to match the reference (10.0pt, currently 12.0pt) — fo:block/@font-size.
2. Set `page.margin-bottom` to match the reference (54.3pt (19.2mm), currently 64.5pt (22.8mm)) — fo:simple-page-master/@margin-bottom (or region-after/@extent).
3. Set `page.margin-left` to match the reference (70.9pt (25.0mm), currently 56.7pt (20.0mm)) — fo:simple-page-master/@margin-left.
4. Set `page.margin-right` to match the reference (51.0pt (18.0mm), currently 57.5pt (20.3mm)) — fo:simple-page-master/@margin-right.
5. Set `page.margin-top` to match the reference (36.2pt (12.8mm), currently 59.6pt (21.0mm)) — fo:simple-page-master/@margin-top (or region-before/@extent).
6. Set `page.running-footer` to match the reference (Issue # — #-#-# ⏐ Page #, currently (none)) — fo:static-content flow-name="xsl-region-after".
7. Set `page.running-header` to match the reference (AIRCRAFT MAINTENANCE MANUAL ⏐ DMC-S#KDPDF-A-#-#-#-#A-#AD, currently (none)) — fo:static-content flow-name="xsl-region-before".
8. Pagination differs (2 against 3). Page-by-page comparisons below the first divergence compare unrelated pages until this is resolved, so fix the geometry and leading findings before reading them.
9. Emit the 8 missing line(s) on page 1, starting with "AIRCRAFT MAINTENANCE MANUAL" (Liberation Serif 8.0pt #000000).
10. The whole page sits -34.3pt from where the reference puts it. Find the first line whose `resid` is non-zero — that is where the displacement is introduced.
11. The reference draws 1 rule/border(s) this rendering does not: top-centre at y=45pt. Look for `@border-*`, `fo:leader` or `@background-color` in the reference's styling.
12. The reference has a running header this rendering has no equivalent of: "AIRCRAFT MAINTENANCE MANUAL / DMC-S#KDPDF-A-#-#-#-#A-#AD". That needs an `fo:static-content` for `xsl-region-before` and an `@extent` on the region.

