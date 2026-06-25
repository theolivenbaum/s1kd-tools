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
}
