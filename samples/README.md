# Sample datasets & test harnesses

This folder holds a **testing dataset for the C# port** of the s1kd-tools,
assembled from five upstream open-source S1000D projects, plus a set of tiny
console programs that drive the ported `S1kdTools.Core` library over that data.

The goal is to evaluate the port against **real-world S1000D content** —
complete CSDBs authored by different toolchains, spanning schema Issues 4.0,
4.2 and 5.0 — rather than only the small synthetic fixtures in
`tests/S1kdTools.Tests`.

## Layout

```
samples/
  README.md                     ← you are here
  datasets/                     ← curated source data (one folder per upstream project)
    fossig/                     ← small self-contained CSDB (Issue 4.2)
    s1000d-spec/                ← the S1000D spec as a CSDB, ~330 DMs (Issue 4.2)
    s1kd-tools-doc/             ← the s1kd-tools documentation as a CSDB (Issue 5.0)
    xsl-stylesheets/            ← Smart Avionics sample publication (Issues 4.0/4.2)
    s1kd2db/                    ← a single documentation DM (Issue 4.2)
  harnesses/                    ← tiny CLI projects that consume the library
    Samples.Common/             ← shared SampleHarness helper (locate data, run a tool, save output)
    Samples.Fossig/             ← one console app per dataset …
    Samples.S1000DSpec/
    Samples.ToolsDoc/
    Samples.XslStylesheets/
    Samples.S1kd2db/
  out/                          ← generated artifacts (checked in; cleared & rebuilt each run)
```

Each `datasets/<name>/` folder has its **own README** documenting exactly what
was taken, from where, and under what license. **No upstream repository was
copied wholesale** — build scaffolding (Ant/Makefiles), pre-rendered PDFs and
rendering-only assets were dropped, build files kept for reference are suffixed
`.upstream`/`.reference`, and the CSDB objects were organised into a consistent
`csdb/` (or `sample/`+`tests/`) shape so the harnesses can find them.

## Provenance & licensing

| Dataset | Upstream | Schema | License |
|---|---|---|---|
| `fossig` | <https://github.com/kibook/FOSSIG> | Issue 4.2 | No explicit license in upstream repo — see folder README |
| `s1000d-spec` | <https://github.com/kibook/S1000D> | Issue 4.2 | No explicit license; content derived from the S1000D specification (© S1000D / ASD-AIA) — reference/testing use only |
| `s1kd-tools-doc` | <https://github.com/kibook/s1kd-tools-doc> | Issue 5.0 | No explicit license in upstream repo — see folder README |
| `xsl-stylesheets` | <https://github.com/kibook/S1000D-XSL-Stylesheets> | Issues 4.0 / 4.2 | MIT-style (© 2010–2011 Smart Avionics Ltd.) — see `xsl-stylesheets/COPYING` |
| `s1kd2db` | <https://github.com/kibook/s1kd2db> | Issue 4.2 | No explicit license in upstream repo — see folder README |

These samples are included **solely for testing and evaluation** of the port.
Where an upstream repository declares no license, it is reproduced here on a
best-effort attribution basis; if you are a rights holder and want a change,
please open an issue. The s1kd-tools maintainer (`kibook`, <http://khzae.net>)
is the author of the FOSSIG, S1000D, s1kd-tools-doc and s1kd2db sample data.

## Running the harnesses

Each harness is an ordinary console project. From the repository root:

```bash
dotnet run --project samples/harnesses/Samples.Fossig
dotnet run --project samples/harnesses/Samples.S1000DSpec
dotnet run --project samples/harnesses/Samples.ToolsDoc
dotnet run --project samples/harnesses/Samples.XslStylesheets
dotnet run --project samples/harnesses/Samples.S1kd2db
```

A harness:

1. locates its dataset under `samples/datasets/<name>/`,
2. runs a sequence of ported tools (`ls`, `validate`, `metadata`, `flatten`,
   `brexcheck`, `refs`, `syncrefs`) over the real objects — the same in-process
   `ITool.Run(...)` entry point the unit tests use,
3. **clears** `samples/out/<name>/` and writes the produced artifacts
   (flattened publications, metadata listings, validation/BREX XML reports)
   into it, and
4. prints a `PASS`/`FAIL` line per step and exits non-zero if any step did not
   produce its **expected** result.

"Expected" is encoded per step. Most steps expect success, but two are
deliberate **negative cases** on the `s1000d-spec` corpus — see that folder's
README — where the *correct* behaviour is to report findings (the dataset
contains data modules with dangling `internalRefId`s and modules that omit a
`reasonForUpdate`, both of which the checkers should and do flag, matching the
upstream C tools). They are written to assert exit code 1 so they read as
`PASS` when the port behaves correctly.

A snapshot of these artifacts is checked in under [`out/`](out/) so the expected
results can be browsed and diffed without running anything. Each harness deletes
its own `out/<name>/` folder at startup and regenerates it, so re-running keeps
the snapshot in sync.

All harness projects are part of `S1kdTools.slnx`, so `dotnet build` builds them
and verifies they compile against the current library API.
