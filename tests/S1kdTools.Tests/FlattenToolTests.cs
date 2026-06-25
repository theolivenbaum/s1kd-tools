using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class FlattenToolTests
{
    // A data module whose code matches the dmRef built below; its content
    // contains a marker string we can assert on after flattening.
    private static string DataModule(string marker) =>
        $"""
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <issueInfo issueNumber="001" inWork="00"/>
              </dmIdent>
            </dmAddress>
          </identAndStatusSection>
          <content>
            <description><para>{marker}</para></description>
          </content>
        </dmodule>
        """;

    private const string DmFileName = "DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML";

    // A publication module referencing the data module above via a dmRef.
    private const string PublicationModule =
        """
        <pm>
          <identAndStatusSection>
            <pmAddress>
              <pmIdent>
                <pmCode modelIdentCode="EX" pmIssuer="12345" pmNumber="00000" pmVolume="00"/>
                <issueInfo issueNumber="001" inWork="00"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
              </pmIdent>
            </pmAddress>
          </identAndStatusSection>
          <content>
            <pmEntry>
              <pmEntryTitle>Chapter 1</pmEntryTitle>
              <dmRef>
                <dmRefIdent>
                  <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                          subSystemCode="0" subSubSystemCode="0" assyCode="00"
                          disassyCode="00" disassyCodeVariant="A" infoCode="040"
                          infoCodeVariant="A" itemLocationCode="D"/>
                  <issueInfo issueNumber="001" inWork="00"/>
                  <language languageIsoCode="en" countryIsoCode="CA"/>
                </dmRefIdent>
              </dmRef>
            </pmEntry>
          </content>
        </pm>
        """;

    private static (int code, string outText, string errText) Run(string dir, params string[] args)
    {
        var tool = new FlattenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Build a small CSDB (PM + DM) in a fresh temp directory.</summary>
    private static string BuildCsdb(string marker = "FLATTENED-CONTENT")
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "PMC-EX-12345-00000-00_001-00_EN-CA.XML"), PublicationModule);
        File.WriteAllText(Path.Combine(dir, DmFileName), DataModule(marker));
        return dir;
    }

    [Fact]
    public void Flatten_InlinesReferencedDataModule()
    {
        string dir = BuildCsdb();
        try
        {
            string pm = Path.Combine(dir, "PMC-EX-12345-00000-00_001-00_EN-CA.XML");
            var (code, outText, _) = Run(dir, "-d", dir, pm);

            Assert.Equal(0, code);
            // The referenced DM's content marker should now appear inline.
            Assert.Contains("FLATTENED-CONTENT", outText);
            // The DM's root element was inlined.
            Assert.Contains("<dmodule>", outText);
            // The original dmRef has been removed.
            Assert.DoesNotContain("<dmRef>", outText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Flatten_XInclude_EmitsIncludeElement()
    {
        string dir = BuildCsdb();
        try
        {
            string pm = Path.Combine(dir, "PMC-EX-12345-00000-00_001-00_EN-CA.XML");
            var (code, outText, _) = Run(dir, "-x", "-d", dir, pm);

            Assert.Equal(0, code);
            Assert.Contains("include", outText);
            Assert.Contains("http://www.w3.org/2001/XInclude", outText);
            Assert.Contains(DmFileName, outText);
            // With XInclude the DM content is referenced, not copied in.
            Assert.DoesNotContain("FLATTENED-CONTENT", outText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Modify_RewritesWithoutInlining()
    {
        // -m keeps refs but does not inline; with no other work the ref stays.
        string dir = BuildCsdb();
        try
        {
            string pm = Path.Combine(dir, "PMC-EX-12345-00000-00_001-00_EN-CA.XML");
            var (code, outText, _) = Run(dir, "-m", "-d", dir, pm);

            Assert.Equal(0, code);
            Assert.DoesNotContain("FLATTENED-CONTENT", outText);
            Assert.Contains("<dmRef>", outText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Remove_UnresolvedReferenceIsDropped()
    {
        // Empty dir: the dmRef cannot be resolved; -D removes it.
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string pm = Path.Combine(dir, "pm.XML");
        File.WriteAllText(pm, PublicationModule);
        try
        {
            var (code, outText, _) = Run(dir, "-D", "-d", dir, pm);

            Assert.Equal(0, code);
            Assert.DoesNotContain("<dmRef>", outText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingReference_WarnsAndKeepsExitZero()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string pm = Path.Combine(dir, "pm.XML");
        File.WriteAllText(pm, PublicationModule);
        try
        {
            var (code, _, errText) = Run(dir, "-d", dir, pm);

            Assert.Equal(0, code);
            Assert.Contains("Could not read referenced object", errText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void RecursiveSearch_FindsDmInSubdirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        string sub = Path.Combine(dir, "dms");
        Directory.CreateDirectory(sub);
        string pm = Path.Combine(dir, "pm.XML");
        File.WriteAllText(pm, PublicationModule);
        File.WriteAllText(Path.Combine(sub, DmFileName), DataModule("DEEP-CONTENT"));
        try
        {
            var (code, outText, _) = Run(dir, "-r", "-d", dir, pm);

            Assert.Equal(0, code);
            Assert.Contains("DEEP-CONTENT", outText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BadPm_ReturnsExitCode1()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        // A document without a <content> child is rejected as a bad PM.
        string pm = Path.Combine(dir, "bad.XML");
        File.WriteAllText(pm, "<pm><identAndStatusSection/></pm>");
        try
        {
            var (code, _, errText) = Run(dir, "-d", dir, pm);

            Assert.Equal(1, code);
            Assert.Contains("Bad publication module", errText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Unique_RemovesDuplicateReferences()
    {
        // Two identical dmRefs in one pmEntry; -m keeps them, -u dedupes.
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-flatten-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string pm = Path.Combine(dir, "pm.XML");
        string dup =
            """
            <pm>
              <content>
                <pmEntry>
                  <pmEntryTitle>T</pmEntryTitle>
                  <dmRef><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A" itemLocationCode="D"/></dmRefIdent></dmRef>
                  <dmRef><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A" itemLocationCode="D"/></dmRefIdent></dmRef>
                </pmEntry>
              </content>
            </pm>
            """;
        File.WriteAllText(pm, dup);
        try
        {
            var (code, outText, _) = Run(dir, "-m", "-u", "-d", dir, pm);

            Assert.Equal(0, code);
            int count = outText.Split("<dmRef>").Length - 1;
            Assert.Equal(1, count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Version_PrintsToolNameAndVersion()
    {
        var tool = new FlattenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("4.0.0", stdout.ToString());
    }

    [Fact]
    public void Help_ListsKeyOptions()
    {
        var tool = new FlattenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-h" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("--use-xinclude", stdout.ToString());
        Assert.Contains("--containers", stdout.ToString());
    }
}
