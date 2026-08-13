using System.Text.Json;
using S1kdTools.Samples.Common;
using S1kdTools.Tools;

// pdfdiff-demo — the reverse-engineering loop, start to finish.
//
// The situation this dataset models: someone hands you a PDF built by a toolchain you do
// not have, along with the S1000D source it was built from, and asks you to reproduce it.
// reference.xsl stands in for that toolchain; you never get to read it. baseline.xsl is
// the placeholder you actually start from, and improved.xsl is what baseline.xsl becomes
// after one round of acting on the report.
//
// The harness renders all three, compares each against the reference, and writes the
// reports. The point of running both baseline and improved is the score: it has to move,
// and move for reasons the report already named, or it is not worth tracking.
var h = new SampleHarness("pdfdiff-demo");

string dm = h.Files("DMC-*.XML").Single();
string outDir = h.OutDir;

string Sheet(string name) => h.Path("stylesheets", name);
string Pdf(string name) => Path.Combine(outDir, name + ".pdf");

// 1. Render the data module three ways.
foreach (string sheet in new[] { "reference", "baseline", "improved" })
{
    h.Run($"render with {sheet}.xsl", new RenderTool(),
        new[] { "-s", Sheet(sheet + ".xsl"), "-o", Pdf(sheet), dm });
}

// 2. Describe the reference on its own. This is the first move when reverse engineering:
//    before anything can be compared, you need the target's paper, margins, fonts and
//    leading written down.
h.Run("describe the reference (layout only)", new PdfDumpTool(),
    new[] { "-s", Pdf("reference") }, saveAs: "reference-style.txt");
h.Run("describe the reference (full structure)", new PdfDumpTool(),
    new[] { Pdf("reference") }, saveAs: "reference-structure.txt");

// 3. Compare each candidate against the reference. Exit 1 is expected — they differ, and
//    that is the whole point; exit 0 would mean the demonstration had nothing to show.
foreach (string candidate in new[] { "baseline", "improved" })
{
    h.Run($"compare {candidate} against the reference", new PdfDiffTool(),
        new[]
        {
            "-o", Path.Combine(outDir, $"{candidate}-vs-reference.md"),
            "-j", Path.Combine(outDir, $"{candidate}-vs-reference.json"),
            "-I", Path.Combine(outDir, $"{candidate}-images"),
            Pdf(candidate), Pdf("reference"),
        },
        expectExit: 1);
}

// 4. The scoreboard: did one round of work actually move the metrics?
Console.WriteLine("--- parity against the reference ---");
double previous = -1;
bool improved = true;
foreach (string candidate in new[] { "baseline", "improved" })
{
    var result = h.Run($"{candidate} parity", new PdfDiffTool(),
        new[] { "-f", "summary", Pdf(candidate), Pdf("reference") }, expectExit: 1);
    string line = result.StdOut.Trim();
    Console.WriteLine($"    {candidate,-9} {line}");

    double parity = Parity(Path.Combine(outDir, $"{candidate}-vs-reference.json"));
    if (previous >= 0 && parity <= previous)
    {
        Console.WriteLine($"    !! parity did not improve: {previous:F1} -> {parity:F1}");
        improved = false;
    }
    previous = parity;
}
Console.WriteLine();

if (!improved)
{
    Console.Error.WriteLine("pdfdiff-demo: the improved stylesheet did not score higher than the "
                            + "baseline, which means the score is not tracking what it claims to.");
}

return h.Summarize() == 0 && improved ? 0 : 1;

static double Parity(string jsonPath)
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
    return doc.RootElement.GetProperty("score").GetProperty("parity").GetDouble();
}
