# FOSSIG sample CSDB

A small, **self-contained S1000D Common Source DataBase (CSDB)** produced by the
Free Open Source Software Interest Group (FOSSIG). Because it is complete yet
tiny, it is the best "happy path" dataset for smoke-testing the port:
everything resolves, validates and BREX-checks cleanly.

## Source

- Upstream: <https://github.com/kibook/FOSSIG>
- Author: `kibook` — <http://khzae.net/1/s1000d/fossig>
- Schema: S1000D **Issue 4.2** (`xml_schema_flat`)

## License

The upstream repository does **not** include an explicit license file. The data
is reproduced here for testing/evaluation of the C# port, with attribution to
the author above. If you are the rights holder and want this changed, please
open an issue.

## Contents

Everything lives under [`csdb/`](csdb/):

| Kind | Count | Notes |
|---|---:|---|
| Data modules (`DMC-*`) | 10 | descriptions (040A), front matter (001/005/009), and the project BREX (022A) |
| Publication module (`PMC-*`) | 1 | `PMC-FOSSIG-KHZAE-00001-00_000-01_EN-CA.XML` |
| Data management list (`DML-*`) | 1 | |
| ICN entities | 5 | `ICN-*.PNG` / `.SVG` / `.TXT` referenced by the modules |

The project BREX is
`DMC-FOSSIG-A-00-00-0000-00A-022A-D_000-01_EN-CA.XML`.

`Makefile.upstream` and `Makefile.csdb.upstream` are the original build files,
kept for reference only (the upstream build renders a README with `s1kd2db` +
`pandoc`, which is outside the scope of this library).

## Exercised by

[`samples/harnesses/Samples.Fossig`](../../harnesses/Samples.Fossig) — runs
`ls`, `validate -x`, `metadata`, `flatten -x -i` (the publication module) and
`brexcheck -b` (against the project BREX). All steps are expected to succeed.
