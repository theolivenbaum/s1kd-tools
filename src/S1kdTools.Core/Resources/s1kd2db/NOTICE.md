# Embedded stylesheet: s1kd2db

`s1kd2db.xsl` converts S1000D data modules to DocBook 5. It is loaded at runtime
by `S1kdTools.DocBook.DocBookConverter` (profile `S1kd2db`, the default).

- **Source:** <https://github.com/kibook/s1kd2db>
- **Author:** `kibook` (maintainer of s1kd-tools) — <http://khzae.net>
- **License:** no explicit license declared upstream; embedded here with
  attribution, consistent with the rest of the s1kd-tools port.

## Local modification (PORT NOTE)

The only change from upstream is a one-line shim: the XSLT 1.0 function
`unparsed-entity-uri()` — which `System.Xml`'s `XslCompiledTransform` does not
implement — is replaced by `ier:resolve()`, bound at runtime to the
`S1kdTools.DocBook.EntityUriResolver` extension object (registered under the
`InfoEntityResolver` namespace). It resolves a graphic entity name to its NDATA
system id from the source document's DTD, matching the original behaviour. The
change is marked inline with a `PORT NOTE` comment in the stylesheet.
