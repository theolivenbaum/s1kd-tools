# Embedded stylesheets: s1000dtodb (Smart Avionics)

The `s1000dtodb.xsl` stylesheet set (12 files) converts S1000D data modules to
DocBook 5 with broad, presentation-oriented coverage (IPD, fault, crew,
procedures, cross-reference tables, configurable content). It is the S1000D →
DocBook half of the Smart Avionics S1000D-XSL-Stylesheets PDF pipeline, loaded
at runtime by `S1kdTools.DocBook.DocBookConverter` (profile `SmartAvionics`).

- **Source:** <https://github.com/kibook/S1000D-XSL-Stylesheets>
  (mirror of Smart Avionics' stylesheets)
- **Copyright:** © 2010–2011 Smart Avionics Ltd.
- **License:** MIT-style permissive — see [`COPYING`](COPYING) (reproduced
  verbatim). GPL-compatible, so it may be embedded in this GPL-3.0 library.

## Local modification (PORT NOTE)

Only `s1000dtodb.xsl` is modified, and only in one place: the single
`unparsed-entity-uri()` call is replaced by `ier:resolve()`, bound at runtime to
the `S1kdTools.DocBook.EntityUriResolver` extension object (the `ier:`/
`InfoEntityResolver` namespace the stylesheet already used for the Java
`InfoEntityResolver`). `System.Xml` cannot evaluate `unparsed-entity-uri()`; the
shim resolves the entity to its DTD system id (and an optional info-entity map),
matching the original. The change is marked inline with a `PORT NOTE` comment.

## Not ported

The `dbtofo` (DocBook → XSL-FO) stage and the `fop`/`s1kd2pdf` rendering step are
**not** part of this port — they require the external DocBook-XSL suite and
Apache FOP (Java). Producing final formats (PDF/HTML) from the DocBook output is
left to existing downstream DocBook tooling.
