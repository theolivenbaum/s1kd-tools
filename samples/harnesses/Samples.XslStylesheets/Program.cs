using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// S1000D-XSL-Stylesheets — Smart Avionics' rendering stylesheets ship with a
// small sample publication (the stylesheet README, marked up in S1000D) plus a
// couple of presentation test data modules. The XSLT rendering itself is out of
// scope for this library port, but the S1000D source files are perfect for
// exercising validate / metadata / flatten on documents from a *different*
// authoring toolchain than the kibook datasets.
var h = new SampleHarness("xsl-stylesheets");

string sampleDir = h.Path("sample");
var sampleDms = h.Files("DMC-*.xml", subDir: "sample");
string pm = h.Files("PMC-*.xml", subDir: "sample").Single();
var testDms = h.Files("DMC-*.XML", subDir: "tests");

// 1. List the sample directory.
h.Run("list sample CSDB objects", new LsTool(),
    new[] { sampleDir }, saveAs: "ls.txt");

// 2. Validate the sample + presentation-test data modules.
h.Run("validate sample data modules", new ValidateTool(),
    sampleDms.Concat(testDms).Prepend("-x").ToArray(), saveAs: "validate-report.xml");

// 3. Extract metadata from the sample data modules.
h.Run("extract metadata (sample DMs)", new MetadataTool(),
    sampleDms, saveAs: "metadata.txt");

// 4. Flatten the sample publication module.
h.Run("flatten sample publication module", new FlattenTool(),
    new[] { "-x", "-i", "-I", sampleDir, pm }, saveAs: "flattened-pm.xml");

return h.Summarize();
