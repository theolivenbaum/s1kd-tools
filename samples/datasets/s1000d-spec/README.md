# S1000D specification CSDB

The S1000D specification itself, authored **as an S1000D CSDB** (Issue 4.2).
With ~330 cross-referenced data modules, a publication module, a DML and a
project BREX, this is the **scale / stress dataset** for the port and the source
of its most interesting real-world edge cases.

## Source

- Upstream: <https://github.com/kibook/S1000D>
- Author: `kibook` — <http://khzae.net>
- Schema: S1000D **Issue 4.2** (`xml_schema_flat`)
- Upstream description: *"a sample S1000D CSDB representing the specification
  itself (Issue 4.2)."*

## License

The upstream repository includes **no explicit license file**. In addition, the
*content* is derived from the **S1000D specification**, which is copyright the
S1000D organisation (managed by ASD/AIA). This copy is included **only for
testing and evaluation** of the port — treat it as reference material, not as a
redistributable specification. If you are a rights holder and want this changed,
please open an issue.

## Contents

Under [`csdb/`](csdb/):

| Kind | Count |
|---|---:|
| Data modules (`DMC-*`) | 330 |
| Publication module (`PMC-*`) | 1 (`PMC-S1000D-B6865-01000-00_001-00_EN-US.XML`) |
| Data management list (`DML-*`) | 1 |
| `.defaults` config | 1 |

The project BREX is
`DMC-S1000D-F-04-10-0301-00A-022A-D_001-00_EN-US.XML`.
`Makefile.upstream` is the original build file (renders a PDF via
`s1kd-flatten | s1kd2pdf`), kept for reference only.

## Known characteristics (used as negative test cases)

This corpus is realistic, which means it is **not pristine** — and that is
useful. Two properties are deliberately asserted by the harness:

1. **Dangling `internalRefId`s.** Several modules (e.g.
   `DMC-S1000D-A-00-00-0000-00A-00UA-A_009-00_EN-US.XML`) reference table IDs
   that are not defined in the same module. `s1kd-validate` correctly reports
   these via the rule `//@internalRefId[not(//@id=.)]` — identical to the
   upstream C tool — so validation of the full set exits **1**.
2. **Missing `reasonForUpdate`.** Most modules are issue > 001 without a
   reason-for-update element, which the spec's own BREX forbids, so
   `brexcheck` reports violations and exits **1**.

Both are wired into the harness with `expectExit: 1`, so they register as
`PASS` precisely *because* the checkers fire correctly on real data.

The publication module pins referenced objects at issue `000-01`, while the
files on disk carry their real issues, so `flatten` and `refs` are run with
`-i` (ignore issue when matching) to resolve every reference.

## Exercised by

[`samples/harnesses/Samples.S1000DSpec`](../../harnesses/Samples.S1000DSpec) —
`ls`, `validate -x` (negative), `metadata`, `flatten -x -i`,
`brexcheck -d -l -x` (negative) and `refs -d -i`.
