# Sample harnesses

One tiny console project per dataset. Each consumes the `S1kdTools.Core` library
directly and "builds" its dataset — i.e. runs the ported tools over the real
upstream CSDB objects and writes the resulting artifacts to
`samples/out/<dataset>/`.

| Project | Dataset | Tools exercised |
|---|---|---|
| `Samples.Fossig` | `fossig` | ls, validate, metadata, flatten, brexcheck, s1kd2db (DocBook) |
| `Samples.S1000DSpec` | `s1000d-spec` | ls, validate, metadata, flatten, brexcheck, refs |
| `Samples.ToolsDoc` | `s1kd-tools-doc` | ls, validate, metadata, flatten, brexcheck |
| `Samples.XslStylesheets` | `xsl-stylesheets` | ls, validate, metadata, flatten |
| `Samples.S1kd2db` | `s1kd2db` | validate, metadata, syncrefs, s1kd2db (DocBook, both profiles) |
| `Samples.Common` | — | shared `SampleHarness` helper (not runnable) |

## How it works

`Samples.Common/SampleHarness.cs` provides the shared driver:

- **`new SampleHarness("<dataset>")`** walks up from the running assembly to
  find `samples/datasets/<dataset>/`, then **deletes and recreates**
  `samples/out/<dataset>/` so each run starts from a clean slate.
- **`h.Files("DMC-*.XML")`** enumerates CSDB objects by glob.
- **`h.Run(title, tool, args, expectExit, saveAs)`** runs a tool through its
  in-process `ITool.Run(args, stdout, stderr)` entry point (exactly what the
  xUnit suite does), captures stdout/stderr, optionally saves stdout to
  `out/<dataset>/<saveAs>`, prints a `PASS`/`FAIL` line and tallies the result.
- **`h.Summarize()`** prints the totals and returns a process exit code
  (0 = every step matched its expected exit code).

The harnesses intentionally invoke tools the same way the library's own tests
do, so they double as an integration smoke test against real data without
spawning a process or touching the CLI front-end.

## Running

```bash
# from the repo root
dotnet run --project samples/harnesses/Samples.Fossig
# … etc, one per dataset
```

Generated output lands in `samples/out/`. A snapshot is checked in, and each
harness clears its own `out/<dataset>/` folder before regenerating, so
re-running keeps the committed snapshot up to date.

## Adding a new harness

1. Add the source data under `samples/datasets/<name>/` with its own README
   (provenance + license).
2. Copy one of the existing project folders, point `new SampleHarness("<name>")`
   at it, and adjust the steps.
3. Register the `.csproj` in `S1kdTools.slnx` under the `/samples/` folder.
