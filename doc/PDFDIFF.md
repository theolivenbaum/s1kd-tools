# Reverse engineering a presentation stylesheet

`s1kd-pdfdiff` and `s1kd-pdfdump` exist for one job: you have a PDF built by a toolchain
you do not have, the S1000D source it was built from, and a stylesheet of your own that
does not yet produce the same thing. These tools measure the gap and describe it in the
vocabulary a stylesheet is written in, so that each round of work is aimed at a named
property rather than at a picture.

Neither has a counterpart in the upstream C s1kd-tools. Both are pure C# — the PDF is
parsed by [PdfPig](https://github.com/UglyToad/PdfPig) (managed, Apache-2.0), and the
rasterisation, diffing, clustering and PNG output are in `S1kdTools.Core/Pdf/`. There is no
native rasteriser, no external process and no scripting runtime involved.

## The loop

```bash
# 1. Look at the target on its own, before comparing anything to it.
s1kd pdfdump -s reference.pdf

# 2. Render your stylesheet and compare.
s1kd render -s mine.xsl -o mine.pdf DMC-….XML
s1kd pdfdiff -o report.md -I diff-images mine.pdf reference.pdf

# 3. Act on ONE finding from "What to change next", then re-run. Watch the score.
s1kd pdfdiff -f summary mine.pdf reference.pdf
# parity=81.5 pages=3/3 words=638/644 text=0.991 pagetext=0.992 ink=0.872 place=0.438 firstdiff=1
```

A worked example, complete with a hidden "other toolchain" stylesheet to reverse engineer,
is in [`samples/datasets/pdfdiff-demo/`](../samples/datasets/pdfdiff-demo/README.md).

## What gets measured

### The parity score

One number out of 100, for tracking progress across iterations. It is a weighted sum of
five agreements, each of which is reported separately because the total on its own does not
tell you what to do:

| component | weight | what it means |
|---|---:|---|
| page count | 20 | `min(pages) ÷ max(pages)` |
| text, document-wide | 30 | word-sequence agreement ignoring page boundaries |
| text, per page | 10 | the same, but the words also have to be on the right page |
| ink quantity | 10 | how much ink each page carries, ours against theirs |
| ink placement | 30 | Jaccard overlap of the inked cells — the layout measure |

They fail in a fixed order, and reading them in that order is most of the method:

1. **Document-wide text agreement first.** Until the right words are being emitted at all,
   nothing about their placement is worth measuring. This is the metric that says whether
   your templates cover the source, and it is blind to layout on purpose.
2. **Then page count.** A page compared against the wrong page produces nonsense in every
   remaining metric, so a pagination difference makes the per-page numbers advisory until
   it is fixed.
3. **Then ink placement.** Only once the right content is on the right page does "is it in
   the right position" mean anything.

Missing pages count as zero in the per-page averages rather than being dropped from the
denominator, so losing a page always costs score.

### Ink

"Ink per page" is the share of the page carrying marks, and the pages are compared as
images — but the images are drawn from the PDF's own content stream rather than by a font
rasteriser. Each measured glyph box, rule, fill and image is painted into a greyscale grid
at 144 dpi (two cells per point), with fractional coverage at the edges.

This is a deliberate trade, and worth understanding before trusting the numbers:

- **What it buys.** The two PDFs come from different toolchains and will almost never embed
  the same font programs. A true rasteriser reports a difference along every glyph edge of
  every *matching* line — the antialiasing noise that makes a raw "83% of pixels differ"
  meaningless. Painting measured ink boxes removes that noise entirely, so the metric
  answers the question a stylesheet author actually has: is the ink in the right place, in
  the right amount?
- **What it costs.** A glyph-shape difference — the wrong typeface at the same size and the
  same widths — is invisible to the ink metrics. It is caught by the structural diff
  instead, as a `font-family` change on the line.

Three numbers come out of it per page: `ink%` (coverage), `ink ratio` (ours ÷ theirs), and
`IoU`, the Jaccard overlap of inked cells. **IoU is the one to watch.** It is 1.0 only when
ink lands in the same places, falls smoothly as content drifts, and — unlike a raw
differing-pixel percentage — is not flattered by the fact that most of a page is blank
paper.

### Clustered difference regions

Differing cells are dilated by 1.5pt, grouped into connected components, and each component
is reported with its box in points, the ink on each side, the text it covers in both
documents, and a guess at what it is: missing or extra ink, a missing rule or border, a
missing fill or shading, or the same ink merely displaced. The classification consults the
actual marks on the page — a region is only called a fill if a fill really covers it — so a
block of text present on one side is not reported as a `background-color` that was never
involved.

Two details matter for reading the list:

- **Regions are located after compensating for a page-wide vertical shift.** A displacement
  of even one point puts a sliver of difference along the top and bottom edge of every line
  on the page; compensating first leaves the residual differences, which are the ones that
  are something other than "everything moved". The *metrics* are always measured unaligned,
  so the shift itself still costs what it should.
- **Boxes are in the reference page's coordinates**, top-left origin, points — the same
  frame the reference structure dump uses.

### The structural diff

Text lines are aligned between the two pages by longest common subsequence, with a fuzzy
second pass so a line whose date format changed is reported as one retexted line rather
than a missing line plus an extra one. Each matched pair yields Δx, Δy, and a **residual**
— Δy with the page-wide shift removed.

The residual is the useful column. When one bad measurement pushes a whole page down, every
line has a large Δy and a residual of zero; the line where the residual first jumps is
where the displacement was introduced, and everything below it is a consequence.

### Style differences

Paper size, margins, body font and size, leading (absolute and as a multiple of the font
size), the text styles by size rank, running headers and footers, indent stops, and the
counts of rules, fills and images — all measured off the ink of both documents and reported
as the property a stylesheet sets, with the XSL-FO attribute that sets it.

These are document-wide, and they are ranked structural / significant / minor. One
structural fix here usually removes a hundred region findings at once, which is why the
report's closing "What to change next" list puts them first.

## Why only the first divergent page is detailed

Metrics always cover every page. Detailed findings stop at the first page that differs.

Differences cascade. A margin that is 28pt out moves everything on page 1, changes where
page 1 ends, and therefore makes pages 2 and 3 compare against entirely different content.
Detailing all three would produce three times the findings, of which two thirds are
restatements of the first page's bug, and would bury it. Fix the first page, re-run, and
the next real problem surfaces on its own.

`--all-pages` overrides this once the first page is clean, and `--detail-pages <n>` takes a
specific number.

## Reading a report

The order of the sections is the order to read them in:

1. **Parity score** — where you are, and which component is furthest from 1.0.
2. **Document metrics** — pages, words, words per page, ink coverage, IoU, region count.
3. **Per-page metrics** — one row per page, with a one-line verdict each. Scan for the
   first row whose verdict is not `MATCH`.
4. **Style differences** — the document-wide properties. Most of the work usually lives
   here.
5. **The first divergent page** — its verdict, its ink regions, its line-by-line changes,
   and a full structure dump of both sides.
6. **What to change next** — the findings ranked by how much else they will move, with the
   instruction to re-run after each one rather than batching them.

The per-page verdicts are a small fixed vocabulary, and each names a different
investigation: `MATCH`, `PAGE GEOMETRY DIFFERS`, `DIFFERENT CONTENT`, `REWRAPPED`,
`CONTENT MISSING`, `EXTRA CONTENT`, `REFLOW CASCADE`, `RESTYLED`, `CONTENT DIFFERS`,
`DIFFERS`.

## Output formats

- **Markdown** (default) — meant to be read, by a person or by an agent.
- **JSON** (`-f json`, or `-j <file>` alongside the Markdown) — schema `s1kd-pdfdiff/1`.
  Stable field names; the line-level changes and page outlines are carried for the detailed
  page only, so the JSON does not outgrow the PDFs it describes.
- **Summary** (`-f summary`) — the one-line progress string, for build logs and progress
  tables.
- **Images** (`-I <dir>`) — for each detailed page, `page-NNN-actual.png`,
  `page-NNN-reference.png`, and `page-NNN-diff.png` (the reference faded, with each
  differing region boxed in red).

## Exit status

`0` when the two renderings agree, `1` when they differ, `2` on a usage or input error.
`--fail-under <n>` changes the gate to "the parity score is at least n", which is what you
want in CI once the output is close enough to be worth protecting from regression.

## `s1kd-pdfdump`

The companion, and the right first move: before there is anything to compare, you need to
know what the target looks like.

```bash
s1kd pdfdump -s reference.pdf     # paper, margins, body font, leading, indents, running heads
s1kd pdfdump reference.pdf        # the above, plus every mark on every page in reading order
s1kd pdfdump -x reference.pdf     # just the text
s1kd pdfdump -J -p 2 reference.pdf  # page 2 as JSON (schema s1kd-pdfdump/1)
```

The default dump interleaves text and graphics in page order, so a rule sits next to the
heading it underlines:

```
   y=   50.0 x=  70.9 w= 137.2    8.0pt Liberation Serif  "AIRCRAFT MAINTENANCE MANUAL"
   y=   50.0 x= 475.0 w=  63.5    8.0pt Liberation Serif  "DMC-S1KDPDF-A-00-00-00-00A-040AD"
   y=   54.1 x=  70.9 w= 467.7  rule h=0.6pt #000000
   y=   95.3 x=  70.9 w=  86.7   14.0pt Liberation Serif,Bold bold  "1. General"
```

Positions are points from the top-left of the page; for text, `y` is the baseline.

## Tuning

The defaults are chosen for A4/Letter technical publications at 8-18pt and rarely need
changing, but every threshold is exposed:

| option | default | when to change it |
|---|---|---|
| `-d, --dpi` | 144 | Lower for very long publications; higher to resolve sub-point differences. |
| `-t, --threshold` | 20 | Raise if faint marks are producing noise. The low default is what lets a 10%-grey panel register at all — its entire signal is 34 levels. |
| `-D, --dilate` | 1.5pt | Raise to merge a paragraph into one region; lower to keep table columns apart. |
| `-m, --min-region` | 0.0004 | Raise to suppress small findings on a busy page. |
| `-T, --tolerance` | 0.75pt | Displacement below this is not called a move. |

## Known limits

- **Glyph shape is not compared.** Same size, same widths, different typeface is invisible
  to the ink metrics; it appears as a `font-family` change in the structural diff.
- **Images are treated as mid-grey blocks.** Whether an image is present, and where, is
  measured; what it depicts is not.
- **Horizontal shift is not compensated** when locating regions, only vertical. Flow layout
  cascades downwards, so this is where the noise is.
- **White marks are ignored.** A white fill paints nothing on white paper, and counting the
  page-sized white rectangles some producers emit would put a difference on every page.
- **Pages are aligned positionally** — page 3 against page 3. When pagination differs, the
  comparison past the first divergence is between unrelated pages, which the report says
  plainly rather than trying to work around.
