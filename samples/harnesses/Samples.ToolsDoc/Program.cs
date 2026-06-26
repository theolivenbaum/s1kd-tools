using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// s1kd-tools-doc — the official documentation of the s1kd-tools, authored *as*
// an S1000D data set (44 description data modules, a PM, a DML and a BREX).
// A good real-world authoring corpus for list/validate/metadata/flatten.
var h = new SampleHarness("s1kd-tools-doc");

string csdb = h.Path("csdb");
string pm = h.Files("PMC-*.XML").Single();
string brex = h.Files("DMC-*-022A-*.XML").Single();
var allDms = h.Files("DMC-*.XML");

// 1. List the CSDB.
h.Run("list CSDB objects", new LsTool(),
    new[] { csdb }, saveAs: "ls.txt");

// 2. Validate every data module (-x: structured XML report on stdout).
h.Run($"validate all data modules ({allDms.Length})", new ValidateTool(),
    allDms.Prepend("-x").ToArray(), saveAs: "validate-report.xml");

// 3. Extract metadata.
h.Run("extract metadata (all DMs)", new MetadataTool(),
    allDms, saveAs: "metadata.txt");

// 4. Flatten the publication module (mirrors the upstream build.sh PDF step,
//    minus the rendering stage which is out of scope for the library).
h.Run("flatten publication module", new FlattenTool(),
    new[] { "-x", "-i", "-I", csdb, pm }, saveAs: "flattened-pm.xml");

// 5. BREX-check the documentation data modules.
h.Run("BREX-check documentation modules", new BrexCheckTool(),
    allDms.Prepend(brex).Prepend("-b").Prepend("-x").ToArray(), saveAs: "brexcheck-report.xml");

return h.Summarize();
