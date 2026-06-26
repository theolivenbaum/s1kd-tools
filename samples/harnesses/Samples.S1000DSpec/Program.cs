using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// S1000D specification CSDB (Issue 4.2) — the largest dataset (~330 data
// modules, a publication module, a DML and a project BREX). This is the
// stress/scale test for the port: it exercises the tools over a realistic,
// fully cross-referenced publication.
var h = new SampleHarness("s1000d-spec");

string csdb = h.Path("csdb");
string pm = h.Files("PMC-*.XML").Single();
var allDms = h.Files("DMC-*.XML");

// 1. List the whole CSDB (latest issue of each object).
h.Run("list CSDB objects", new LsTool(),
    new[] { csdb }, saveAs: "ls.txt");

// 2. Validate every data module. NOTE: this is a deliberate *negative* case.
//    Several spec data modules contain dangling `internalRefId`s (e.g. table
//    references resolved at publication time), so the validator correctly
//    reports them and exits 1 — exactly matching the upstream s1kd-validate.c
//    rule `//@internalRefId[not(//@id=.)]`. We assert exit 1 to prove the
//    IDREF checker actually fires on real data.
h.Run($"validate all data modules ({allDms.Length}) — expects IDREF findings",
    new ValidateTool(), allDms.Prepend("-x").ToArray(),
    expectExit: 1, saveAs: "validate-report.xml");

// 3. Extract metadata for every data module.
h.Run("extract metadata (all DMs)", new MetadataTool(),
    allDms, saveAs: "metadata.txt");

// 4. Flatten the publication module — resolves every dmRef to a file and
//    rewrites it as an XInclude (upstream Makefile: `s1kd-flatten -x -I csdb`).
h.Run("flatten publication module", new FlattenTool(),
    new[] { "-x", "-i", "-I", csdb, pm }, saveAs: "flattened-pm.xml");

// 5. BREX-check every module against the BREX it references (`-d csdb -l`
//    resolves each module's own brexDmRef and any layered BREX). This is also
//    a *negative* case: most spec modules are issue > 001 without a
//    reasonForUpdate element, which the spec's own BREX forbids, so brexcheck
//    correctly reports violations and exits 1.
h.Run("BREX-check all modules against their BREX — expects findings",
    new BrexCheckTool(),
    allDms.Prepend("-x").Prepend("-l").Prepend(csdb).Prepend("-d").ToArray(),
    expectExit: 1, saveAs: "brexcheck-report.xml");

// 6. List references found in the publication module and match them against
//    the CSDB. `-i` ignores issue info when matching (the PM pins issue 000-01
//    while the files carry their real issues), so every reference resolves.
h.Run("list & resolve references in the PM", new RefsTool(),
    new[] { "-d", csdb, "-i", pm }, saveAs: "refs.txt");

return h.Summarize();
