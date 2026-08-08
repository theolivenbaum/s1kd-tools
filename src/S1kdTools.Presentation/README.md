# S1kdTools.Presentation

Ready-made XSL-FO presentation stylesheets for **every S1000D CSDB object type**,
and a small API that turns a CSDB object into a page-oriented PDF.

The stylesheets produce the layout of a civil aircraft technical publication:
publisher and publication title in the running header, object code, issue and
page count in the footer, an identification and status title block, ATA-style
hierarchical step numbering (`1.` / `A.` / `(1)` / `(a)`), boxed warnings and
cautions, change bars in the start margin, and the fixed table shapes each
schema calls for — job set-up information for a procedure, the parts list of an
illustrated parts catalogue, the answer/action tree of a fault isolation
procedure, and so on.

Rendering is done by [`S1kdTools.Core`](https://www.nuget.org/packages/S1kdTools.Core)
(`s1kd render`), which drives the [FOP.Sharp](https://www.nuget.org/packages/FOP.Sharp)
layout engine in-process — no external XSL-FO processor, no Java.

## Use

```csharp
using S1kdTools.Presentation;

var dm = S1000DPresentation.Load("DMC-A350X-A-27-81-00-00A-720A-A_002-00_EN-GB.XML");

// The object type is detected from the object itself.
CsdbObjectType type = S1000DPresentation.DetectObjectType(dm);   // Procedure

using Stream pdf = S1000DPresentation.RenderToPdf(dm);
```

Straight to a file, or to any of the other formats FOP.Sharp supports:

```csharp
using var file = File.Create("task.pdf");
S1000DPresentation.RenderToPdf(dm, file);

using Stream markdown = S1000DPresentation.Render(dm, RenderTool.RenderFormat.Markdown);
```

Several objects render into one document — the XSL-FO is merged and laid out
once, so the result is a single continuous publication:

```csharp
using Stream publication = S1000DPresentation.RenderToPdf([frontMatter, descriptive, task, ipd]);
```

## Options

Every layout knob is an XSLT parameter, so the same settings apply whether you
render through this API or run one of the stylesheets yourself through
`s1kd render -s`:

```csharp
var options = new PresentationOptions
{
    Publisher = "AIRBUS S.A.S.",            // header, left; taken from the object when null
    PublicationTitle = "AIRCRAFT MAINTENANCE MANUAL",
    Page = PageSize.A4,
    Margins = PageMargins.Default,
    FontFamily = "Helvetica",
    FontSizePt = 9,
    IncludeTitleBlock = true,
    Watermark = "DRAFT",
    GraphicsDirectories = ["csdb/icn"],     // where the referenced ICNs live
};

using Stream pdf = S1000DPresentation.RenderToPdf(dm, options);
```

**Illustrations.** A `graphic` element names an ICN, not a file. Point
`GraphicsDirectories` at the directories that hold them and each reference is
resolved before the transform; an ICN that is not found renders as a labelled
placeholder frame rather than failing the render.

## The stylesheets

One stylesheet per schema, all importing `common.xsl` — which carries the page
masters, the title block, and the constructs S1000D shares between schemas
(paragraphs, levelled paragraphs, lists, CALS tables, figures, warnings,
cautions, notes, references, applicability annotations, change marks and the
procedural constructs).

They are embedded in the assembly and reachable by name, so a house style starts
as a copy:

```csharp
string xsl = S1000DPresentation.GetStylesheet(CsdbObjectType.Procedure);
File.WriteAllText("my-proced.xsl", xsl);   // edit, then use it with s1kd render -s
```

Covered object types:

| Data modules | | Other CSDB objects |
| --- | --- | --- |
| `descript` `proced` `fault` `ipd` | `crew` `schedul` `checklist` `process` | `pm` publication module |
| `sb` service bulletin | `brex` `brdoc` business rules | `dml` data management list |
| `frontmatter` `container` | `techrep` `comrep` repositories | `ddn` data dispatch note |
| `wrngdata` `wrngflds` wiring | `learning` `scocontent` training | `comment` |
| `appliccrossreftable` `prdcrossreftable` `condcrossreftable` | | `icnmetadata` `scormcontentpackage` `update` |

Nothing in an object is dropped: an element a stylesheet does not claim is still
printed by a generic fall-back that labels leaves from their element names.

## Renderer notes

Two behaviours of the FOP.Sharp layout engine shaped these stylesheets, and are
worth knowing if you write your own:

- **A word boundary opens at every `fo:inline` edge**, so a reference wrapped in
  an inline would be followed by a space before its punctuation. The reference
  constructs here emit plain text instead, and inlines are used only where
  styling needs them.
- **Long unbroken tokens are never split.** An XPath in a table cell would
  overrun its column, so the stylesheets set paths one location step per line
  (`path-lines` in `common.xsl`).

Illustrations must be given as plain file paths — a `file://` URI in
`fo:external-graphic` renders as an empty frame — which is what
`PrepareForPresentation` writes.
