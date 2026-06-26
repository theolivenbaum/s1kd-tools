using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// s1kd2db — an XSLT that converts S1000D data modules to DocBook 5. The
// conversion stylesheet itself is *not* part of this C# library port (it is a
// downstream rendering concern, kept here as `s1kd2db.xsl.reference`), but the
// project ships one S1000D documentation data module which we can still
// validate, read metadata from and syncrefs over with the ported tools.
var h = new SampleHarness("s1kd2db");

var dms = h.Files("DMC-*.XML", subDir: ".");
string dm = dms.Single();

// 1. Validate the documentation data module.
h.Run("validate documentation module", new ValidateTool(),
    new[] { "-x", dm }, saveAs: "validate-report.xml");

// 2. Extract its metadata.
h.Run("extract metadata", new MetadataTool(),
    new[] { dm }, saveAs: "metadata.txt");

// 3. Rebuild its References table from the references in the module
//    (output to stdout — the sample file is never modified in place).
h.Run("sync references table", new SyncrefsTool(),
    new[] { dm }, saveAs: "syncrefs.xml");

return h.Summarize();
