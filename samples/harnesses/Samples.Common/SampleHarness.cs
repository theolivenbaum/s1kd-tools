using System.Diagnostics;
using S1kdTools.Tools;

namespace S1kdTools.Samples.Common;

/// <summary>
/// Tiny test-driver shared by the per-dataset sample harnesses.
///
/// Each harness is a small console program that consumes the
/// <c>S1kdTools.Core</c> library directly (the same in-process
/// <see cref="ITool.Run"/> entry point the unit tests use) and runs a sequence
/// of tools over one of the curated datasets under <c>samples/datasets/</c>.
///
/// "Building the sample files" means: run the ported tools over the real
/// upstream CSDB objects and emit the resulting artifacts (flattened
/// publications, metadata listings, validation/BREX reports) under
/// <c>samples/out/&lt;dataset&gt;/</c>. The exit code is non-zero if any step
/// that was expected to succeed did not, so the harness doubles as a smoke
/// test for the C# port against real-world data.
/// </summary>
public sealed class SampleHarness
{
    public string DatasetName { get; }
    public string DatasetDir { get; }
    public string OutDir { get; }

    private int _pass;
    private int _fail;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public SampleHarness(string datasetName)
    {
        DatasetName = datasetName;
        string samplesRoot = LocateSamplesRoot();
        DatasetDir = System.IO.Path.Combine(samplesRoot, "datasets", datasetName);
        if (!Directory.Exists(DatasetDir))
            throw new DirectoryNotFoundException($"Dataset folder not found: {DatasetDir}");
        OutDir = System.IO.Path.Combine(samplesRoot, "out", datasetName);
        Directory.CreateDirectory(OutDir);

        Console.WriteLine($"=== sample harness: {datasetName} ===");
        Console.WriteLine($"    dataset : {DatasetDir}");
        Console.WriteLine($"    output  : {OutDir}");
        Console.WriteLine();
    }

    /// <summary>Absolute path to a file/dir inside this dataset.</summary>
    public string Path(params string[] parts) =>
        System.IO.Path.Combine(new[] { DatasetDir }.Concat(parts).ToArray());

    /// <summary>All CSDB object files matching a glob inside the dataset (sorted).</summary>
    public string[] Files(string searchPattern, string subDir = "csdb") =>
        Directory.EnumerateFiles(System.IO.Path.Combine(DatasetDir, subDir), searchPattern)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Run a tool, capture its output, optionally persist stdout to
    /// <c>out/&lt;dataset&gt;/&lt;saveAs&gt;</c>, and check the exit code.
    /// </summary>
    public StepResult Run(string title, ITool tool, string[] args, int expectExit = 0, string? saveAs = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code;
        try
        {
            code = tool.Run(args, stdout, stderr);
        }
        catch (Exception ex)
        {
            code = int.MinValue;
            stderr.Write(ex);
        }

        string outText = stdout.ToString();
        string errText = stderr.ToString();

        if (saveAs is not null)
            File.WriteAllText(System.IO.Path.Combine(OutDir, saveAs), outText);

        bool ok = code == expectExit;
        if (ok) _pass++; else _fail++;

        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {title}");
        Console.WriteLine($"       s1kd {tool.Name} {string.Join(' ', args.Select(Short))}");
        Console.WriteLine($"       exit={code} (expected {expectExit})" +
                          $"  stdout={outText.Length}B  stderr={errText.Length}B" +
                          (saveAs is not null ? $"  -> {saveAs}" : ""));
        foreach (var line in FirstLines(errText, 3))
            Console.WriteLine($"         | {line}");
        Console.WriteLine();

        return new StepResult(ok, code, outText, errText);
    }

    /// <summary>Print a summary banner and return a process exit code.</summary>
    public int Summarize()
    {
        _clock.Stop();
        Console.WriteLine($"--- {DatasetName}: {_pass} passed, {_fail} failed " +
                          $"in {_clock.ElapsedMilliseconds} ms ---");
        return _fail == 0 ? 0 : 1;
    }

    private static string Short(string arg)
    {
        // Keep the printed command readable: collapse long absolute paths to basenames.
        if (arg.Length > 0 && (arg.Contains('/') || arg.Contains('\\')))
            return System.IO.Path.GetFileName(arg.TrimEnd('/', '\\'));
        return arg;
    }

    private static IEnumerable<string> FirstLines(string text, int n) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(n).Select(l => l.TrimEnd('\r'));

    /// <summary>
    /// Walk up from the running assembly to find the repository's
    /// <c>samples/</c> directory (the one that contains <c>datasets/</c>).
    /// </summary>
    private static string LocateSamplesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "samples", "datasets");
            if (Directory.Exists(candidate))
                return System.IO.Path.Combine(dir.FullName, "samples");
            // Also handle being run from inside samples/ itself.
            if (string.Equals(dir.Name, "samples", StringComparison.Ordinal) &&
                Directory.Exists(System.IO.Path.Combine(dir.FullName, "datasets")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the 'samples/datasets' directory from " + AppContext.BaseDirectory);
    }
}

/// <summary>Outcome of a single harness step.</summary>
public readonly record struct StepResult(bool Ok, int ExitCode, string StdOut, string StdErr);
