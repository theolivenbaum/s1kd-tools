# s1kd-tools documentation CSDB

The official **documentation of the s1kd-tools**, written as an S1000D data set
(Issue 5.0). It is a realistic authoring corpus produced *by* the tools, and the
only dataset here on **Issue 5.0**, so it exercises the port against the newer
schema.

## Source

- Upstream: <https://github.com/kibook/s1kd-tools-doc>
- Author: `kibook` — maintainer of <https://github.com/kibook/s1kd-tools>
- Schema: S1000D **Issue 5.0** (`xml_schema_flat`) — one BREX module references
  4.2.

## License

The upstream repository includes **no explicit license file**. The s1kd-tools
project it documents is GPL-3.0-or-later, but the documentation data modules
carry no separate license declaration. Reproduced here for testing/evaluation
with attribution; open an issue if you are the rights holder and want a change.

## Contents

Under [`csdb/`](csdb/):

| Kind | Count | Notes |
|---|---:|---|
| Data modules (`DMC-*`) | 44 | one description DM per tool, plus front matter and the BREX (022A) |
| Publication module (`PMC-*`) | 1 | `PMC-S1KDTOOLS-KHZAE-00000-00_EN-CA.XML` |
| Data management list (`DML-*`) | 1 | |
| ICN entities | 2 | `ICN-*.PNG` |

The BREX is `DMC-S1KDTOOLS-A-00-00-00-00A-022A-D_EN-CA.XML`. Note the filenames
omit issue/inwork info (a valid S1000D naming variant), which is itself a useful
test of the tools' filename parsing.

## Exercised by

[`samples/harnesses/Samples.ToolsDoc`](../../harnesses/Samples.ToolsDoc) —
`ls`, `validate -x`, `metadata`, `flatten -x -i` and `brexcheck -b`. All steps
are expected to succeed.
