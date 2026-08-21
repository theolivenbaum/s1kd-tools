# CLAUDE.md — s1kd-tools C# port

This file orients Claude (and humans) working in this repository. It describes
what we are building, how the code is organised, and the conventions to follow.

## What this project is

The **s1kd-tools** are a set of small command-line tools for creating and
manipulating [S1000D](http://www.s1000d.org) data (technical publications for
the aerospace/defence ILS world). The upstream project is written in C against
**libxml2** and **libxslt**. This repository is a **port to C# / .NET**.

The original C source has been preserved verbatim under [`reference/`](reference/)
and is the authoritative specification of behaviour. When porting a tool, read
its C source under `reference/tools/<tool>/` first and mirror its semantics,
option flags, exit codes, and output format. Each tool also has a manpage under
`reference/tools/<tool>/doc/*.1` and a `README.md`.

> **Golden rule:** the C code is the spec. If the port and the C disagree, the
> C is right (unless the C has a known bug — note it in `todo.md`).

## Repository layout

```
/                         Project docs (README, INTRO, TUTORIAL, …) + license
/reference/               The original C implementation (read-only reference)
  tools/common/           s1kd_tools.c/.h — shared utilities (ported to Core)
  tools/libs1kd/          Public C library + existing C# P/Invoke bindings
  tools/s1kd-*/           One directory per tool (source, docs, templates, tests)
/src/
  S1kdTools.Core/         Ported shared library + programmatic API (≈ libs1kd)
  S1kdTools.Cli/          Command-line front-end (the `s1kd` executable)
  S1kdTools.Editor.Server/ The editor's back-end: AddS1kdEditor / MapS1kdEditor
  S1kdTools.Editor/       Tesserae components for editing a CSDB object (Transpose)
/samples/editor/          The editor sample: a host, a demo front-end, and a CSDB
/tests/
  S1kdTools.Tests/        xUnit test project
  editor-e2e/             Playwright tests for the editor (Node)
/S1kdTools.slnx            Solution (net10.0: library, CLI, tests, editor back-end)
/S1kdTools.Editor.slnx     Solution (Transpose: the editor components + demo app)
/CLAUDE.md  /todo.md      This file and the porting task list
```

**Two solutions on purpose.** Everything in `S1kdTools.slnx` is net10.0 and needs
nothing but the SDK, so `dotnet test S1kdTools.slnx` — which CI runs, and which has
nothing to do with a browser — works on a bare machine. The two projects compiled
to JavaScript by the [Transpose](https://www.nuget.org/packages/Transpose.Compiler)
compiler live in their own solution; the back-end that serves them is in the main
one.

## Technology mapping (C → C#)

The C code is built on libxml2's DOM + XPath + XSLT. We map onto the BCL
`System.Xml` stack, which is the closest semantic match:

| C / libxml2                         | C# / .NET                                   |
|-------------------------------------|---------------------------------------------|
| `xmlDocPtr`                         | `System.Xml.XmlDocument`                    |
| `xmlNodePtr`                        | `System.Xml.XmlNode` / `XmlElement`         |
| `xmlXPathEvalExpression`            | `XmlNode.SelectNodes` / `SelectSingleNode`  |
| `xsltApplyStylesheet`               | `System.Xml.Xsl.XslCompiledTransform`       |
| `xmlReadFile` / `xmlSaveFile`       | `XmlDocument.Load` / `.Save`                |
| `xmlSchemaValidate`                 | `XmlReaderSettings` + `XmlSchemaSet`        |
| `BAD_CAST "…"` (xmlChar*)           | plain `string`                              |
| `getopt_long`                       | manual parser in `S1kdTools.Cli` (see below)|

Notes / gotchas:
- **XSLT version:** libxslt is XSLT 1.0 + EXSLT. `XslCompiledTransform` is XSLT
  1.0 and supports script/extension objects but **not** all EXSLT functions
  natively. Stylesheets that use EXSLT (`str:`, `exsl:`, `dyn:`) may need an
  extension-object shim — see `S1kdTools.Core/Xslt/`. Flag any unsupported
  construct in `todo.md`.
- **Embedded resources:** the C build embeds `*.xsl`, templates, and `.xml`
  data files into each executable with `xxd -i`. In .NET these become
  **embedded resources** (`<EmbeddedResource>`), loaded via
  `Assembly.GetManifestResourceStream`. Keep the original files; reference them
  from the project file rather than copying their bytes.
- **Namespaces:** S1000D XPath in the C code is written against the default
  (no-prefix) namespace because the documents are typically un-namespaced.
  Preserve that. Use an `XmlNamespaceManager` only where the C registers one.
- **Exit codes:** each C tool `#define`s its `EXIT_*` codes. Reproduce them
  exactly; tests assert on them.
- **Whitespace / formatting:** `xmlSaveFile` formatting differs subtly from
  `XmlDocument.Save`. Where tests compare serialized output, normalise or use
  `XmlWriterSettings` to match (no BOM, `\n` line endings, 2-space indent off by
  default — match the C output for that tool).

### Rendering (FOP.Sharp)

The .NET port adds one tool with **no C counterpart**: `s1kd-render`
(`Tools/RenderTool.cs`). Upstream s1kd-tools leave rendering to an external
XSL-FO processor (Apache FOP, run as a separate Java process); the port brings
that in-process via the [`FOP.Sharp`](https://www.nuget.org/packages/FOP.Sharp)
NuGet package (a C# port of Apache FOP), referenced from `S1kdTools.Core`. A
presentation stylesheet (`-s`) transforms a CSDB object into XSL-FO, which
FOP.Sharp renders to one of its supported targets: **PDF** (`FopProcessor` /
the native PdfSharp-free renderer), plain **text**, **Markdown** or **HTML**
(`Fop.Render.Text.*`). Rendering is stream-based: the core entry point is
`RenderTool.Render(Stream foInput, Stream output, format, fontDirs, native)`
(backed by FOP.Sharp's `Convert(Stream, Stream)` methods), with a
`Render(string foXml, …) → byte[]` convenience wrapper. The CLI writes straight
to the destination stream (a file or stdout) and handles format inference and
output naming around it. Multiple inputs given with an explicit `-o` are merged
into one FO document (`RenderTool.MergeFo`: unioned `layout-master-set`,
concatenated `page-sequence`s) and rendered once — a single combined PDF.

### Comparing renderings (PdfPig)

Two further tools with no C counterpart — `s1kd-pdfdiff` (`Tools/PdfDiffTool.cs`)
and `s1kd-pdfdump` (`Tools/PdfDumpTool.cs`) — read a *rendered* PDF back and
compare two of them, for reverse-engineering a presentation stylesheet from a
PDF produced by some other toolchain. Parsing is
[PdfPig](https://github.com/UglyToad/PdfPig) (`PackageReference` on
`S1kdTools.Core`, Apache-2.0, pure managed); everything else lives in
`S1kdTools.Core/Pdf/`:

| file | responsibility |
|---|---|
| `PdfPageModel.cs` | the model: pages, text lines/words, graphic marks, `Rect` |
| `PdfExtractor.cs` | PdfPig → model, converting to **top-left points** |
| `PageStyleFacts.cs` | derived layout: margins, body font, leading, indents, running heads |
| `InkRaster.cs` | model → greyscale ink grid (144 dpi default) |
| `InkDiff.cs` | pixel diff, dilation, connected-component clustering, region classification |
| `StructureDiff.cs` | LCS line alignment → missing/extra/moved/restyled/retexted |
| `StyleDelta.cs` | style facts diff → findings with XSL-FO hints |
| `PdfComparison.cs` | orchestration, document metrics, the parity score |
| `MarkdownReport.cs`, `JsonReport.cs` | report writers |
| `PngWriter.cs` | greyscale/RGB PNG via `ZLibStream` |

Conventions worth preserving when working here:

- **Coordinates are top-left points**, converted once in `PdfExtractor`. PDF's
  bottom-left origin never escapes that file.
- **The raster is synthetic** — measured ink boxes, not a font rasteriser — so
  that two toolchains' different font programs do not produce a difference on
  every glyph edge. `InkDiffOptions.Threshold` is low (20) for the same reason:
  there is no antialiasing noise to reject.
- **Metrics are measured unaligned; regions are clustered shift-compensated.**
  The score must cost a displaced page what it should, while the region list
  must not restate the displacement once per line.
- **Detail stops at the first divergent page** by default (`DetailPages = 1`).
  Metrics always cover every page.
- The parity score's weights live in `PdfComparer`; changing them changes an
  interface people track across iterations, so change them deliberately.

See `doc/PDFDIFF.md` for the user-facing explanation and
`samples/datasets/pdfdiff-demo/` for the end-to-end example.

### Editing (the WYSIWYG editor)

The port adds an editor for CSDB objects, which the C tools have no counterpart
for. It is built on a second stylesheet family alongside the presentation ones:
`Resources/editing/edit.xsl` projects an object into an **addressed tree of
editable blocks** — every paragraph, step and field paired with the XPath of the
element it came from — and `S1kdTools.Core/Editing/` turns an edit made against a
block back into a change to that element.

| file | responsibility |
|---|---|
| `Resources/editing/edit.xsl` | the projection: blocks, runs, labels, insert points |
| `EditModel.cs` | the model a front-end draws: `EditDocument`/`EditBlock`/`EditRun` |
| `EditProjection.cs` | run the stylesheet, read its output |
| `EditCommands.cs` | apply an edit to the XML |
| `EditTemplateCatalogue.cs` | what a new element is made of, and what may go where |
| `EditStylesheet.cs` | where a projection comes from: assembly, file, string, stream, transform |
| `EditProfile.cs` | the two together — which S1000D dialect this editor speaks |
| `EditPalette.cs` | the component catalogue, each entry projected |
| `EditSession.cs` | one open document: apply, undo, redo, serialize |
| `../ResourceResolver.cs` | `IResourceResolver`: where a name turns into bytes (not editing-only) |

Conventions worth preserving when working here:

- **The XML is the document of record and everything else is derived from it.**
  A command mutates the XML; the model is re-projected from the result. There is no
  second representation to keep in step, which is what makes it impossible for the
  editor to show something the file does not say.
- **A block's path is generated exactly as `XmlUtils.XPathOf` generates one.** The
  stylesheet writes them and `EditCommands` resolves them; a disagreement would
  apply an edit to the wrong element.
- **A path is only valid against the revision it was projected from.** Inserting a
  paragraph renumbers its later siblings, so every mutating call returns a fresh
  model and callers must not reuse paths across one.
- **An element the author did not retype is put back, not recreated.** Each run
  carries `Src`, the position of the child element it came from, so an untouched
  `dmRef` survives an edit to the sentence around it as the same node.
- **Nothing refuses an edit.** A module being written is invalid most of the time;
  validity is reported by the check (schema well-formedness, BREX, and whether it
  can be laid out), never enforced by the editor.
- **Which parts of an object are editable is a stylesheet's decision, not a
  program's.** Everything takes an `EditProfile`, and a house stylesheet resolves
  its `xsl:import` against this assembly — so a project overrides a few templates
  rather than forking. An element with no template still appears through a
  fall-through; measured over `samples/datasets`, 99.9% of authorable text is
  reachable on schemas nobody wrote templates for.
- **Nothing assumes a name is a path.** Editing stylesheets and their imports,
  presentation stylesheets and their imports, and illustrations all go through
  `IResourceResolver` (`ResourceResolver.cs`), so a CSDB in a content management
  system, an object store or a zip is a resolver rather than a fork. `LocalPath` is
  on that interface for one measured reason: FOP.Sharp resolves an
  `external-graphic` by file path and treats a `data:` URI exactly as a missing
  file, so bytes with no path are materialized to a temporary file for one layout —
  which is why `EditorPresentation.TransformToFo` returns a disposable
  `PresentationFo`. Do not simplify that back to a bare `XmlDocument`.

The HTTP half is `src/S1kdTools.Editor.Server` — the session store, the check, the
page layout and the endpoints, as `AddS1kdEditor` / `MapS1kdEditor`. The sample
host is configuration only; if you are adding server behaviour it almost certainly
belongs in the package rather than beside it.

See `doc/EDITOR.md` for the user-facing explanation, `src/S1kdTools.Editor/` for
the browser components, and `samples/editor/` for the running sample.

## CLI conventions

The C project ships one executable per tool (`s1kd-newdm`, `s1kd-metadata`, …).
The .NET port provides a single `s1kd` executable with sub-commands:

```
s1kd <tool> [options] [files]      e.g.  s1kd metadata -n issueNumber FILE.XML
```

For drop-in compatibility the host also performs **multi-call dispatch**: if it
is invoked via a name like `s1kd-metadata` (argv[0]), it routes to that tool, so
symlinks/renames reproduce the original command names.

Each tool is a class in `S1kdTools.Core` (namespace `S1kdTools.Tools`) exposing
a programmatic API plus an `int Run(IReadOnlyList<string> args, …)` entry point
used by the CLI. This keeps the logic library-testable without spawning a
process.

## Conventions for this codebase

- Target framework: **net10.0** (SDK present in CI). Library uses
  `net8.0`-compatible APIs where practical so it can be multi-targeted later.
- `Nullable` and `ImplicitUsings` enabled. Treat warnings seriously.
- Naming: idiomatic C# (`PascalCase` types/methods). Keep the S1000D domain
  vocabulary from the C (e.g. `IsInRange`, `EvalApplic`, `DataModuleCode`).
- Prefer pure, testable functions in `Core`; keep `Console`/IO at the edges
  (CLI layer) so tools can be unit-tested.
- Every ported tool gets at least one xUnit test exercising a real fixture
  (reuse fixtures from `reference/.../examples` and `tests` where they exist).

## Build & test

```
dotnet build S1kdTools.slnx
dotnet test  S1kdTools.slnx
dotnet run --project src/S1kdTools.Cli -- <tool> [args]
```

The editor is a second solution, and needs the Transpose compiler:

```
dotnet tool install --global Transpose.Compiler
dotnet build S1kdTools.Editor.slnx
dotnet run --project samples/editor/S1kdTools.EditorServer   # then open localhost:5000
cd tests/editor-e2e && npm install && npx playwright test
```

A trap worth knowing when working on `src/S1kdTools.Editor`: the surface's
stylesheet is a Transpose *resource*, named in `tps.json`, and MSBuild watches
`tps.json` but not the files it points at. The csproj adds the stylesheet to
`MSBuildAllProjects` so a CSS-only change forces a rebuild; without that the old
stylesheet stays in every consuming app's output, silently.

## Where to start when porting a tool

1. Read `reference/tools/<tool>/<tool>.c` and its manpage.
2. List its options, exit codes, and the XPath/XSLT it relies on.
3. Add a `Tools/<Tool>.cs` in `Core`; reuse helpers in `Csdb`, `XmlUtils`,
   `Applicability`, `Metadata` rather than re-implementing.
4. Register it in the CLI dispatcher (`S1kdTools.Cli/Program.cs`).
5. Add tests; update `todo.md` (tick it off, note deviations).
