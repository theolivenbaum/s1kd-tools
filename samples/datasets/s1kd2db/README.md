# s1kd2db — documentation data module

`s1kd2db` is an XSLT that converts S1000D data modules to **DocBook 5** (for
downstream conversion via tools like pandoc). Its stylesheet is now **ported**:
it is embedded in `S1kdTools.Core` and run in-process via
`S1kdTools.DocBook.DocBookConverter` (and the `s1kd s1kd2db` command) — see
[Porting note](#porting-note) below. The project also ships one S1000D
documentation data module that the ported tools validate, read and convert.

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
- `s1kd2db.xsl.reference` — the upstream S1000D→DocBook stylesheet, kept here as
  the historical reference. The actual ported copy lives (embedded) at
  `src/S1kdTools.Core/Resources/s1kd2db/s1kd2db.xsl`.

## Porting note

The conversion is done in-process on `XslCompiledTransform` — no `xsltproc`, no
Java. `DocBookConverter` offers two profiles: **`S1kd2db`** (this stylesheet,
the lean default) and **`SmartAvionics`** (the broader `s1000dtodb` set from the
[xsl-stylesheets](../xsl-stylesheets) dataset). The only modification to the
upstream stylesheet is a one-line shim replacing `unparsed-entity-uri()` (which
System.Xml lacks) with a .NET entity-resolver extension; see the embedded
stylesheet's `PORT NOTE` and `NOTICE.md`. Rendering DocBook to a final format
(PDF/HTML) remains a downstream concern for existing DocBook tooling.

## Exercised by

[`samples/harnesses/Samples.S1kd2db`](../../harnesses/Samples.S1kd2db) —
`validate -x`, `metadata`, `syncrefs` (to stdout; the sample is never modified
in place) and **DocBook conversion** with both profiles. All steps are expected
to succeed.
