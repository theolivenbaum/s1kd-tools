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
/tests/
  S1kdTools.Tests/        xUnit test project
/S1kdTools.slnx            Solution
/CLAUDE.md  /todo.md      This file and the porting task list
```

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
(`Fop.Render.Text.*`). The pure entry point is
`RenderTool.Render(foXml, format, fontDirs, native) → byte[]`; the CLI handles
IO, format inference and per-input output naming around it.

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

## Where to start when porting a tool

1. Read `reference/tools/<tool>/<tool>.c` and its manpage.
2. List its options, exit codes, and the XPath/XSLT it relies on.
3. Add a `Tools/<Tool>.cs` in `Core`; reuse helpers in `Csdb`, `XmlUtils`,
   `Applicability`, `Metadata` rather than re-implementing.
4. Register it in the CLI dispatcher (`S1kdTools.Cli/Program.cs`).
5. Add tests; update `todo.md` (tick it off, note deviations).
