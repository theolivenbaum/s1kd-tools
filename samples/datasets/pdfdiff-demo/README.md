# pdfdiff-demo — reverse engineering a presentation stylesheet

A worked example for `s1kd-pdfdiff` and `s1kd-pdfdump`. Unlike the other datasets here,
nothing in this one is third-party: it was written for the demonstration, so the "target"
stylesheet can be hidden from the exercise and revealed afterwards.

## The situation it models

Someone hands you a PDF built by a toolchain you do not have, along with the S1000D source
it was built from, and asks you to reproduce it. You cannot read their stylesheet. All you
can do is look at what came out and work backwards.

## Contents

- [`csdb/`](csdb/) — one descriptive data module (S1000D Issue 4.2), the hydraulic power
  system: three sections, a warning, a caution, a note, a random list and a CALS table.
  Enough structure that a stylesheet has real decisions to make about it.
- [`stylesheets/reference.xsl`](stylesheets/reference.xsl) — **the toolchain you do not
  have.** A4 with asymmetric margins, a ruled running header carrying the technical name
  and the data module code, a ruled footer with issue date and folio, three levels of
  numbered headings, a page break before each top-level section, a shaded and bordered
  warning box, a ruled table with a shaded header row, a hanging-indent list, 10pt body on
  12.5pt leading, justified. Renders to **3 pages**.
- [`stylesheets/baseline.xsl`](stylesheets/baseline.xsl) — **where you start.** Every word
  of the data module, in one column, at 12pt, with default margins and no page furniture.
  Not wrong; just uninformed.
- [`stylesheets/improved.xsl`](stylesheets/improved.xsl) — **one round later.** `baseline`
  after acting on the first report's "What to change next" list, and nothing else. The
  warning box, table rules, list labels and ruled title block are still missing.

## Running it

```
dotnet run --project samples/harnesses/Samples.PdfDiff
```

Everything lands in `samples/out/pdfdiff-demo/`: the three PDFs, a description of the
reference on its own, a Markdown and a JSON report per candidate, and the per-page diff
images.

## What it shows

The score, and the fact that it moves for reasons the report already named:

```
baseline  parity=47.1 pages=2/3 words=601/644 text=0.932 pagetext=0.182 ink=0.217 place=0.062
improved  parity=81.5 pages=3/3 words=638/644 text=0.991 pagetext=0.992 ink=0.872 place=0.438
```

Read the components rather than the total. `text=0.932` on the baseline says the content
was very nearly all there from the start — the placeholder stylesheet was never the
problem. `place=0.062` says almost none of it was in the right place. One round of work on
the page master and the page furniture took pagination from 2 pages to 3 and placement from
6% to 44%, and left the remaining gap where the report said it would be: the table, the
warning box and the list labels.

The harness fails if `improved` does not outscore `baseline`. A progress metric that does
not go up when the work goes well is not one.

## Where the page-count difference comes from

`reference.xsl` starts each top-level section on a new page (`break-before="page"`), which
is an editorial decision some toolchains make and some do not — and it is exactly the kind
of decision a comparison has to be able to surface, because it changes the page count
before it changes anything else. `baseline.xsl` makes no such decision, so its content
flows continuously and falls where it falls: 2 pages against the reference's 3.

Both stylesheets rely on ordinary pagination for everything else; content that overruns the
bottom of a region carries onto the next page normally, including within a single block.
