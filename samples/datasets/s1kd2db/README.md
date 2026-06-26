# s1kd2db — documentation data module

`s1kd2db` is an XSLT that converts S1000D data modules to **DocBook 5** (for
downstream conversion via tools like pandoc). The conversion stylesheet is *not*
part of this C# library port — it is a downstream rendering concern — but the
project ships one S1000D documentation data module that the ported tools can
still validate, read and process.

## Source

- Upstream: <https://github.com/kibook/s1kd2db>
- Author: `kibook` — <http://khzae.net>
- Schema: S1000D **Issue 4.2**

## License

The upstream repository includes **no explicit license file**. Reproduced here
for testing/evaluation with attribution; open an issue if you are the rights
holder and want a change.

## Contents

- `DMC-S1KD2DB-A-00-00-00-00A-040A-D_000-01_EN-CA.XML` — the project's
  documentation data module (a "process" DM, info code 040, item location D).
- `s1kd2db.xsl.reference` — the upstream S1000D→DocBook stylesheet, included
  **for reference only**; it is not consumed by the harness or the port.

## Exercised by

[`samples/harnesses/Samples.S1kd2db`](../../harnesses/Samples.S1kd2db) —
`validate -x`, `metadata` and `syncrefs` (rebuilding the References table to
stdout; the sample file is never modified in place). All steps are expected to
succeed.
