using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// FOSSIG — a small, self-contained CSDB (data modules, a publication module, a
// data-management list, ICN entities and a project BREX). It is the ideal
// "happy path" smoke test: list it, validate it, read its metadata, flatten the
// publication and BREX-check the content against the project's own business
// rules.
var h = new SampleHarness("fossig");

string csdb = h.Path("csdb");
string pm = h.Files("PMC-*.XML").Single();
string brex = h.Files("DMC-*-022A-*.XML").Single();   // the FOSSIG project BREX
var contentDms = h.Files("DMC-*.XML");

// 1. List the CSDB objects (latest issue of each).
h.Run("list CSDB objects", new LsTool(),
    new[] { csdb }, saveAs: "ls.txt");

// 2. Validate every data module (well-formedness + IDREF integrity).
//    `-x` emits a structured XML report on stdout. This dataset is clean,
//    so validation succeeds (exit 0).
h.Run("validate all data modules", new ValidateTool(),
    contentDms.Prepend("-x").ToArray(), saveAs: "validate-report.xml");

// 3. Dump the metadata of every data module (list mode).
h.Run("extract metadata (all DMs)", new MetadataTool(),
    contentDms, saveAs: "metadata.txt");

// 4. Flatten the publication module into a single XInclude'd document
//    (mirrors the upstream Makefile's `s1kd-flatten -x -I csdb` step;
//    `-i` matches the latest issue of each referenced object).
h.Run("flatten publication module", new FlattenTool(),
    new[] { "-x", "-i", "-I", csdb, pm }, saveAs: "flattened-pm.xml");

// 5. BREX-check the content data modules against the project BREX.
h.Run("BREX-check content data modules", new BrexCheckTool(),
    contentDms.Prepend(brex).Prepend("-b").Prepend("-x").ToArray(),
    saveAs: "brexcheck-report.xml");

return h.Summarize();
