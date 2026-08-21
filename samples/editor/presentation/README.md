# Presentation stylesheets

XSL-FO presentation stylesheets — one per S1000D CSDB object type, plus the shared
`common.xsl` they all `xsl:import`. They lay a CSDB object out as a page of a civil
aircraft technical publication: running header and footer, an identification and
status title block, ATA-style step numbering (`1.` / `A.` / `(1)` / `(a)`), boxed
warnings and cautions, change bars in the start margin, and the fixed table shapes
each schema calls for.

**They are a house style, not a part of S1000D.** S1000D says what a data module
contains, not what its page looks like; every publisher decides the second for
themselves. These stylesheets are one such decision — a plausible one for a civil
aircraft maintenance publication — and they are here so the editor sample has a
real presentation layer to preview against instead of a stylesheet written to make
a demo look plausible.

**They are the sample's, to change.** `S1kdTools.EditorServer` reads them from this
folder at run time and compiles them on first use, so editing one and refreshing
the browser changes the preview. That is deliberate: how a warning box looks is a
publishing decision, and the sample should demonstrate that it is one file away.

The editor's *own* projection — what the WYSIWYG surface draws — is a different
stylesheet family living in the library:
`src/S1kdTools.Core/Resources/editing/edit.xsl`.

## Which stylesheet is used

The server picks `<schema>.xsl`, where `<schema>` is the base name of the object's
own `xsi:noNamespaceSchemaLocation` — so a data module declaring
`…/xml_schema_flat/proced.xsd` is presented by `proced.xsl`. An object whose schema
has no stylesheet here still opens in the editor; it has no page preview, and the
check endpoint says so rather than failing silently.
