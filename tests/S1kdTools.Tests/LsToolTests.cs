using System.Runtime.InteropServices;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class LsToolTests : IDisposable
{
    private readonly string _dir;

    public LsToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"s1kd-ls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string name) =>
        File.WriteAllText(Path.Combine(_dir, name), "<dmodule/>");

    private void TouchContent(string name, string xml) =>
        File.WriteAllText(Path.Combine(_dir, name), xml);

    private (int code, string[] lines) Run(params string[] args)
    {
        var tool = new LsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { _dir };
        int code = tool.Run(all, stdout, stderr);
        var lines = stdout.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.GetFileName(p)!)
            .ToArray();
        return (code, lines);
    }

    [Fact]
    public void ListsOnlyDataModules_WithDFlag()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        Touch("PMC-EX-12345-00001-00_001-00_EN-CA.XML");
        Touch("readme.txt");

        var (code, lines) = Run("-D");
        Assert.Equal(0, code);
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML" }, lines);
    }

    [Fact]
    public void DefaultListsAllCsdbObjects_NotOtherFiles()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        Touch("PMC-EX-12345-00001-00_001-00_EN-CA.XML");
        Touch("notes.md");

        var (_, lines) = Run();
        Assert.Contains("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML", lines);
        Assert.Contains("PMC-EX-12345-00001-00_001-00_EN-CA.XML", lines);
        Assert.DoesNotContain("notes.md", lines);
    }

    [Fact]
    public void OtherFlag_ListsNonCsdbFiles()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        Touch("notes.md");

        var (_, lines) = Run("-n");
        Assert.Equal(new[] { "notes.md" }, lines);
    }

    [Fact]
    public void OfficialFilter_ExcludesInwork()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML"); // official
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML"); // inwork

        var (_, official) = Run("-D", "-i");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML" }, official);

        var (_, inwork) = Run("-D", "-I");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML" }, inwork);
    }

    [Fact]
    public void LatestFilter_KeepsHighestIssue()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        Touch("DMC-EX-A-00-00-00-00A-040A-D_002-00_EN-CA.XML");

        var (_, latest) = Run("-D", "-l");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_002-00_EN-CA.XML" }, latest);

        var (_, old) = Run("-D", "-o");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML" }, old);
    }

    [Fact]
    public void NullSeparator_UsesNul()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        var tool = new LsTool();
        var stdout = new StringWriter();
        tool.Run(new[] { "-D", "-0", _dir }, stdout, new StringWriter());
        Assert.Contains('\0', stdout.ToString());
    }

    // ----- -N (omit-issue): inwork state read from the object's XML -----

    [Fact]
    public void OmitIssue_ReadsInworkFromFileContent()
    {
        // Filenames omit issue/inwork info; the inwork state lives in the XML.
        TouchContent("DMC-EX-A-00-00-00-00A-040A-D_EN-CA.XML",
            "<dmodule><issueInfo issueNumber=\"001\" inWork=\"00\"/></dmodule>"); // official
        TouchContent("DMC-EX-A-00-00-01-00A-040A-D_EN-CA.XML",
            "<dmodule><issueInfo issueNumber=\"001\" inWork=\"03\"/></dmodule>"); // inwork

        var (_, official) = Run("-D", "-N", "-i");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_EN-CA.XML" }, official);

        var (_, inwork) = Run("-D", "-N", "-I");
        Assert.Equal(new[] { "DMC-EX-A-00-00-01-00A-040A-D_EN-CA.XML" }, inwork);
    }

    [Fact]
    public void OmitIssue_NoInworkAttribute_IsOfficial()
    {
        TouchContent("DMC-EX-A-00-00-00-00A-040A-D_EN-CA.XML", "<dmodule/>");

        var (_, official) = Run("-D", "-N", "-i");
        Assert.Equal(new[] { "DMC-EX-A-00-00-00-00A-040A-D_EN-CA.XML" }, official);

        var (_, inwork) = Run("-D", "-N", "-I");
        Assert.Empty(inwork);
    }

    // ----- -e (exec): expand {} and run a command per object -----

    [Fact]
    public void BuildExecCommand_SubstitutesBraces()
    {
        Assert.Equal("cat /path/to/obj.xml",
            LsTool.BuildExecCommand("cat {}", "/path/to/obj.xml"));
        // Multiple placeholders all expand to the path.
        Assert.Equal("cp a.xml a.xml",
            LsTool.BuildExecCommand("cp {} {}", "a.xml"));
        // No placeholder leaves the command unchanged.
        Assert.Equal("echo hi", LsTool.BuildExecCommand("echo hi", "a.xml"));
    }

    [Fact]
    public void Exec_RunsCommandForEachObject_AndSuppressesListing()
    {
        Touch("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        Touch("DMC-EX-A-00-00-01-00A-040A-D_001-00_EN-CA.XML");

        string outFile = Path.Combine(_dir, "exec-out.txt");
        // Append each object's path to a file via the system shell.
        string cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"echo {{}}>>\"{outFile}\""
            : $"echo {{}} >> \"{outFile}\"";

        var tool = new LsTool();
        var stdout = new StringWriter();
        int code = tool.Run(new[] { "-D", "-e", cmd, _dir }, stdout, new StringWriter());

        Assert.Equal(0, code);
        // In exec mode nothing is printed to stdout for the matched objects.
        Assert.DoesNotContain("DMC-EX", stdout.ToString());

        Assert.True(File.Exists(outFile));
        string[] lines = File.ReadAllLines(outFile)
            .Where(l => l.Trim().Length > 0)
            .ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.Contains("00-00-00"));
        Assert.Contains(lines, l => l.Contains("00-00-01"));
    }
}
