using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class SnsToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new SnsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // A small BREX-style document carrying an SNS structure with two systems,
    // sub-systems and one sub-sub-system, matching the real snsRules layout.
    private const string Brex =
        """
        <dmodule>
          <brex>
            <contextRules>
              <snsRules>
                <snsDescr>
                  <snsSystem>
                    <snsCode>00</snsCode>
                    <snsTitle>Product, General</snsTitle>
                    <snsSubSystem>
                      <snsCode>0</snsCode>
                      <snsTitle>Product, Description</snsTitle>
                    </snsSubSystem>
                    <snsSubSystem>
                      <snsCode>4</snsCode>
                      <snsTitle>Technical publication</snsTitle>
                      <snsSubSubSystem>
                        <snsCode>1</snsCode>
                        <snsTitle>Publications</snsTitle>
                      </snsSubSubSystem>
                    </snsSubSystem>
                  </snsSystem>
                  <snsSystem>
                    <snsCode>04</snsCode>
                    <snsTitle>Worthiness limitations</snsTitle>
                  </snsSystem>
                </snsDescr>
              </snsRules>
            </contextRules>
          </brex>
        </dmodule>
        """;

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-sns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Setup_CreatesNamedDirectoryTree()
    {
        string work = NewTempDir();
        try
        {
            string brexPath = Path.Combine(work, "brex.xml");
            File.WriteAllText(brexPath, Brex);

            string outdir = Path.Combine(work, "SNS");
            var (code, _, err) = Run("-D", work, "-d", outdir, brexPath);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, err);

            // Top-level systems named "code - title".
            string sys00 = Path.Combine(outdir, "00 - Product, General");
            string sys04 = Path.Combine(outdir, "04 - Worthiness limitations");
            Assert.True(Directory.Exists(sys00), "system 00 dir");
            Assert.True(Directory.Exists(sys04), "system 04 dir");

            // Nested sub-system and sub-sub-system.
            string sub0 = Path.Combine(sys00, "0 - Product, Description");
            string sub4 = Path.Combine(sys00, "4 - Technical publication");
            string subsub1 = Path.Combine(sub4, "1 - Publications");
            Assert.True(Directory.Exists(sub0), "subsystem 0 dir");
            Assert.True(Directory.Exists(sub4), "subsystem 4 dir");
            Assert.True(Directory.Exists(subsub1), "subsubsystem 1 dir");
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void OnlyCode_NamesDirectoriesWithCodeOnly()
    {
        string work = NewTempDir();
        try
        {
            string brexPath = Path.Combine(work, "brex.xml");
            File.WriteAllText(brexPath, Brex);

            string outdir = Path.Combine(work, "SNS");
            var (code, _, _) = Run("-n", "-D", work, "-d", outdir, brexPath);

            Assert.Equal(0, code);
            Assert.True(Directory.Exists(Path.Combine(outdir, "00")), "code-only system dir");
            Assert.True(Directory.Exists(Path.Combine(outdir, "00", "4", "1")), "code-only nested dir");
            // The titled variant must NOT exist.
            Assert.False(Directory.Exists(Path.Combine(outdir, "00 - Product, General")));
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void Copy_PlacesDataModuleIntoMatchingSnsDirectory()
    {
        string work = NewTempDir();
        try
        {
            string brexPath = Path.Combine(work, "brex.xml");
            File.WriteAllText(brexPath, Brex);

            // A DM with systemCode=00, subSystem=4, subSubSystem=1, assy=0000.
            const string dmName = "DMC-EX-A-00-41-0000-00A-040A-D_001-00_EN-CA.XML";
            File.WriteAllText(Path.Combine(work, dmName), "<dmodule/>");

            string outdir = Path.Combine(work, "SNS");
            var (code, _, err) = Run("-c", "-D", work, "-d", outdir, brexPath);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, err);

            // It should be filed under 00 -> 4 -> 1 (deepest matching level).
            string placed = Path.Combine(outdir,
                "00 - Product, General",
                "4 - Technical publication",
                "1 - Publications",
                dmName);
            Assert.True(File.Exists(placed), "DM placed in deepest matching SNS dir");
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void Print_OutputsIndentedSns()
    {
        string work = NewTempDir();
        try
        {
            string brexPath = Path.Combine(work, "brex.xml");
            File.WriteAllText(brexPath, Brex);

            var (code, outText, _) = Run("-p", brexPath);

            Assert.Equal(0, code);
            Assert.Contains("00 - Product, General", outText);
            Assert.Contains("    0 - Product, Description", outText);
            Assert.Contains("        1 - Publications", outText);
            Assert.Contains("04 - Worthiness limitations", outText);
            // No directory tree should have been created in print mode.
            Assert.False(Directory.Exists(Path.Combine(work, "SNS")));
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void MissingBrex_ReturnsNoBrexExitCode()
    {
        string work = NewTempDir();
        try
        {
            var (code, _, err) = Run(Path.Combine(work, "does-not-exist.xml"));
            Assert.Equal(3, code);
            Assert.Contains("Could not read BREX", err);
        }
        finally { Directory.Delete(work, true); }
    }
}
