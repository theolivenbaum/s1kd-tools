# Rendered presentation samples

**These files are generated.** Each PDF is one sample CSDB object from
[`tests/S1kdTools.Presentation.Tests/Samples/`](../../../tests/S1kdTools.Presentation.Tests/Samples)
rendered through the presentation stylesheet for its type, by the test
`RenderSamplesTests.EverySampleRendersToPdf`. They are checked in as a reference
snapshot so the output of each stylesheet can be looked at without running
anything.

There is one PDF per S1000D object type — 29 in all — plus
`merged-publication.pdf`, which is three of the objects rendered into a single
continuous document to exercise the XSL-FO merge.

Do not edit by hand. To refresh the snapshot:

```bash
dotnet test tests/S1kdTools.Presentation.Tests
```

The sample objects describe a fictitious A350X slat control and monitoring
system. Every identifier in them — data module codes, part numbers, CAGE codes,
serial numbers, modification and service bulletin numbers — is invented for the
sample set.
