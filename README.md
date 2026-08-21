![s1kd-tools](doc/ICN-S1KDTOOLS-A-000000-A-KHZAE-00001-A-002-01.PNG)

A set of small, free and open source software tools for manipulating
[S1000D](http://www.s1000d.org) data.

> **C# port in progress.** This repository is being ported from the original
> C implementation to C# / .NET. The original C source is preserved under
> [`reference/`](reference/) and remains the authoritative spec. The .NET
> solution lives under [`src/`](src/) and [`tests/`](tests/). See
> [`CLAUDE.md`](CLAUDE.md) for the architecture and [`todo.md`](todo.md) for
> per-tool porting progress.
>
> ```
> dotnet build              # build the solution (S1kdTools.slnx)
> dotnet test               # run the test suite
> dotnet run --project src/S1kdTools.Cli -- metadata -n issueInfo FILE.XML
> ```
>
> ### Packaging
>
> The core library (`S1kdTools.Core`) is published as a NuGet package and the
> `s1kd` CLI as a single self-contained (or framework-dependent) executable:
>
> ```
> # Build the S1kdTools.Core NuGet package (.nupkg lands in bin/Release/)
> dotnet pack src/S1kdTools.Core/S1kdTools.Core.csproj -c Release
>
> # Build a single-file CLI executable for a runtime (RID), self-contained:
> dotnet publish src/S1kdTools.Cli -c Release -r linux-x64 \
>     -p:PublishSingleFile=true --self-contained
>
> # ...or framework-dependent (smaller; requires the .NET runtime to be present):
> dotnet publish src/S1kdTools.Cli -c Release -r linux-x64 \
>     -p:PublishSingleFile=true --self-contained=false
> ```
>
> Supported RIDs include `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`,
> `osx-x64`, `osx-arm64`. The published single file (`s1kd` / `s1kd.exe`) lands
> in `src/S1kdTools.Cli/bin/Release/net10.0/<RID>/publish/`. The embedded XSLT,
> templates and data resources are read from the assembly's manifest, so they
> resolve correctly when bundled into the single-file host.
>
> ### Rendering (`s1kd-render`)
>
> Beyond the ported C tools, the .NET edition adds an `s1kd-render` tool that
> renders CSDB objects to a presentation format in-process, using the
> [FOP.Sharp](https://www.nuget.org/packages/FOP.Sharp) engine (the C# port of
> Apache FOP). A presentation stylesheet transforms the object into XSL-FO,
> which is then rendered to one of FOP's output targets: **PDF**, plain
> **text**, **Markdown** or **HTML**.
>
> ```
> # Transform a data module with a presentation stylesheet, then render to PDF:
> s1kd render -s presentation.xsl -o DM.pdf DMC-EXAMPLE-….XML
>
> # Render an existing XSL-FO document to Markdown (format inferred from -o):
> s1kd render -F -o out.md document.fo
>
> # Pass stylesheet parameters and pick the format explicitly:
> s1kd render -s style.xsl -p lang=en -t html -o DM.html DMC-….XML
>
> # Merge a whole set of data modules into ONE combined PDF:
> s1kd render -s presentation.xsl -o manual.pdf DMC-*.XML
> ```
>
> **Multiple inputs.** With an explicit `-o`, every input object is transformed
> to XSL-FO and the results are *merged into a single document* — the page
> masters are unioned and each object's `fo:page-sequence`s are concatenated, so
> a set of data modules renders as one continuous PDF (one publication). Without
> `-o`, each input renders to its own file named after it; a lone object on
> stdin renders to stdout.
>
> Run `s1kd render --help` for the full option list (`-F` for XSL-FO input,
> `-t` format, `-d` font directories, `-n` native PDF renderer, `-p`
> stylesheet parameters).
>
> ### Comparing renderings (`s1kd-pdfdiff`, `s1kd-pdfdump`)
>
> Two more tools with no C counterpart, for the job of **reverse engineering a
> presentation stylesheet**: you have a PDF built by a toolchain you do not have,
> the S1000D source it was built from, and a stylesheet of your own that does not
> yet produce the same thing.
>
> ```
> # Describe the target: paper, margins, body font, leading, indents, running heads.
> s1kd pdfdump -s reference.pdf
>
> # Compare your rendering against it and write a report plus diff images.
> s1kd pdfdiff -o report.md -I diff-images mine.pdf reference.pdf
>
> # One line for a build log or a progress table.
> s1kd pdfdiff -f summary mine.pdf reference.pdf
> # parity=81.5 pages=3/3 words=638/644 text=0.991 ink=0.872 place=0.438 firstdiff=1
> ```
>
> `pdfdiff` measures the whole document — page count, words per page, ink per
> page, and how much of the ink lands in the same place — into a single parity
> score out of 100 built to be tracked across iterations, then takes the **first
> divergent page** apart: clustered regions of differing ink with a guess at what
> each one is, a line-by-line diff with displacements and style changes, a
> structure dump of both sides, and the document-wide style differences stated as
> the XSL-FO properties that set them. Detail stops at the first divergent page
> by default because differences cascade; `-a` details them all.
>
> Both tools are pure C# — [PdfPig](https://github.com/UglyToad/PdfPig) parses
> the PDF, and the rasterisation, clustering and PNG output are in
> `S1kdTools.Core/Pdf/`. No native rasteriser, no external process.
>
> See **[doc/PDFDIFF.md](doc/PDFDIFF.md)** for what each metric means and how to
> read a report, and
> [`samples/datasets/pdfdiff-demo/`](samples/datasets/pdfdiff-demo/README.md) for
> a worked example.
>
> ### Editing (the WYSIWYG editor)
>
> The port also adds an **editor**: a data module open in a browser, drawn as the
> page it will be published as and typed into in place, with its source and its
> rendered PDF beside it and a rail of components to drag into it.
>
> ```
> dotnet build S1kdTools.Editor.slnx
> dotnet run --project samples/editor/S1kdTools.EditorServer   # then open localhost:5000
> ```
>
> It rests on a second stylesheet family alongside the presentation ones.
> `Resources/editing/edit.xsl` projects a CSDB object into an **addressed tree of
> editable blocks** — every paragraph, step and field paired with the XPath of the
> element it came from, and the number the page will print beside it — and
> `S1kdTools.Core/Editing/` turns an edit made against a block back into a change
> to that element. Programmatically:
>
> ```csharp
> var session = EditSession.Open("DMC-….XML");
> EditBlock step = session.Model.AllBlocks().First(b => b.Kind == "step");
> session.Apply(EditCommand.Insert(step.Path, EditPositions.LastChild, "warning"));
> session.Undo();
> ```
>
> The XML stays the document of record: a command mutates it and the model is
> re-projected from the result, so there is no second representation that can be
> right when the file is wrong. A reference the author did not retype is *moved*
> back into place rather than rebuilt, so it survives an edit to the sentence
> around it with everything on it intact.
>
> Standing one up is two packages and about ten lines — the back-end is
> [`S1kdTools.Editor.Server`](src/S1kdTools.Editor.Server), the browser half is
> [`S1kdTools.Editor`](src/S1kdTools.Editor):
>
> ```csharp
> builder.Services.AddS1kdEditor(new EditorOptions { CsdbDirectory = "csdb" });
> app.MapS1kdEditor();
> ```
>
> The front-end holds no S1000D knowledge at all, and the vocabulary it speaks is a
> stylesheet away from being a different one: a project passes an `EditProfile`
> whose stylesheet imports ours and overrides the templates it disagrees with.
>
> See **[doc/EDITOR.md](doc/EDITOR.md)** for the design and
> [`samples/editor/`](samples/editor/README.md) for the running sample.

  - [Introduction](INTRO.md)

  - [Installation](INSTALL.md)

  - [Basic S1000D tutorial](TUTORIAL.md)

  - [Usage examples](EXAMPLE.md)

  - [List of .defaults file identifiers](DEFAULTS.md)

Some examples of S1000D data sets produced with these tools are
available here:

  - [s1kd-tools-doc](http://github.com/kibook/s1kd-tools-doc)

  - [FOSSIG](http://github.com/kibook/FOSSIG)

  - [S1000D spec sample](http://github.com/kibook/S1000D)

These tools are primarily developed around Issue 6 of the specification,
and are generally compatible with the previous 5.0 and 4.X issues.
Support for Issue 3.0 and lower is a work-in-progress. Support for SGML
schemas is not planned.

  - [Compatibility with each issue of the
    specification](COMPATIBILITY.md)

Additional links:

  - [s1kd-tools GitHub repository](http://github.com/kibook/s1kd-tools)

  - [s1kd-tools GitLab repository](http://gitlab.com/kibukj/s1kd-tools)

  - [s1kd-tools GitHub Pages site](http://kibook.github.io/s1kd-tools)

  - [s1kd-tools on khzae.net](http://khzae.net/1/s1000d/s1kd-tools)
