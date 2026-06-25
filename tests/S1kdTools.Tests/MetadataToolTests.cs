using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class MetadataToolTests
{
    private static (int code, string outText, string errText) Run(string fixturePath, params string[] args)
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { fixturePath };
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-md-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, Fixtures.DataModule);
        return path;
    }

    [Fact]
    public void GetByName_PrintsValue()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-n", "techName");
            Assert.Equal(0, code);
            Assert.Equal("Example\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetMissing_ReturnsExitCode4()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-n", "pmTitle");
            Assert.Equal(4, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetWithOverwrite_PersistsChange()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-n", "techName", "-v", "Renamed", "-f");
            Assert.Equal(0, code);

            var (_, outText, _) = Run(path, "-n", "techName");
            Assert.Equal("Renamed\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowAll_IncludesKnownKeys()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);
            Assert.Contains("techName", outText);
            Assert.Contains("issueInfo", outText);
            Assert.Contains("Example", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Info_ListsKeys()
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-H" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("securityClassification", stdout.ToString());
    }
}
