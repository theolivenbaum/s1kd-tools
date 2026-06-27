# S1000D XSL Stylesheets — sample publication

S1000D source files shipped with Smart Avionics' **S1000D XSL Stylesheets**
(the rendering pipeline behind `s1kd2pdf`). The bundled S1000D documents are
valuable because they come from a **different authoring toolchain** than the
kibook datasets and mix schema **Issues 4.0 and 4.2**.

The project's **S1000D → DocBook** stage (`s1000dtodb`) **is ported**: it is
embedded in `S1kdTools.Core` and available as the `SmartAvionics` profile of
`S1kdTools.DocBook.DocBookConverter` (`s1kd s1kd2db -S`). The **DocBook → XSL-FO
→ PDF** stage (`dbtofo` + Apache FOP) is *not* ported — it needs the external
DocBook-XSL suite and a Java FO formatter; final rendering is left to downstream
DocBook tooling.

## Source

- Upstream: <https://github.com/kibook/S1000D-XSL-Stylesheets>
  (mirror of Smart Avionics' stylesheets)
- Copyright: © 2010–2011 **Smart Avionics Ltd.**
- Schema: S1000D **Issue 4.0** (presentation tests) and **Issue 4.2** (sample)

## License

This is the **only** dataset here with an explicit license. The upstream
`COPYING` file (reproduced verbatim as [`COPYING`](COPYING)) grants a permissive,
MIT-style license with two conditions worth noting: derived stylesheets that are
publicly distributed must be renamed/re-versioned, and contributors' names may
not be used for promotion without permission. Those conditions concern the
*stylesheets*; only the S1000D sample data is reproduced here.

## Contents

- [`sample/`](sample/) — the stylesheet README marked up in S1000D
  (`S1000DXSL-README.xml`), two description data modules and the sample
  publication module `PMC-S1000DXSL-SMART-00001-00.xml`, plus the
  `info-entity-map.txt` and logo asset they reference. `build.xml.upstream` is
  the original Ant build, kept for reference only.
- [`tests/`](tests/) — two presentation-oriented data modules
  (`*-000A-*`, Issue 4.0) used upstream to test rendering. The pre-rendered
  `.pdf` companions were **not** copied.

## Exercised by

[`samples/harnesses/Samples.XslStylesheets`](../../harnesses/Samples.XslStylesheets)
— `ls`, `validate -x`, `metadata` and `flatten -x -i` over the sample and test
data modules. All steps are expected to succeed.
