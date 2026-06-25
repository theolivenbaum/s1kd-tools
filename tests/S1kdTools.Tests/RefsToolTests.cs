using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class RefsToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new RefsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // A data module that references another DM, a PM, an external pub, a comment,
    // a DML and an ICN.
    private const string ReferencingDm =
        """
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
            <refs>
              <dmRef>
                <dmRefIdent>
                  <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                          subSystemCode="0" subSubSystemCode="0" assyCode="01"
                          disassyCode="00" disassyCodeVariant="A" infoCode="040"
                          infoCodeVariant="A" itemLocationCode="D"/>
                  <issueInfo issueNumber="001" inWork="00"/>
                </dmRefIdent>
              </dmRef>
              <pmRef>
                <pmRefIdent>
                  <pmCode modelIdentCode="EX" pmIssuer="12345" pmNumber="00001" pmVolume="00"/>
                  <issueInfo issueNumber="001" inWork="00"/>
                </pmRefIdent>
              </pmRef>
              <commentRef>
                <commentRefIdent>
                  <commentCode modelIdentCode="EX" senderIdent="12345"
                               yearOfDataIssue="2026" seqNumber="00001" commentType="Q"/>
                </commentRefIdent>
              </commentRef>
              <dmlRef>
                <dmlRefIdent>
                  <dmlCode modelIdentCode="EX" senderIdent="12345" dmlType="c"
                           yearOfDataIssue="2026" seqNumber="00001"/>
                </dmlRefIdent>
              </dmlRef>
              <externalPubRef>
                <externalPubRefIdent>
                  <externalPubCode>ABC-12345</externalPubCode>
                </externalPubRefIdent>
              </externalPubRef>
              <infoEntityRef infoEntityRefIdent="ICN-EX-A-000000-A-00001-A-001-01"/>
            </refs>
          </content>
        </dmodule>
        """;

    private static string WriteFixture(string dir, string baseName, string xml)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, baseName);
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void ListsAllReferenceCodes_NoMatch()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "DMC-EX-A-00-0-0-00-00A-040A-D_001-00_EN-CA.XML", ReferencingDm);

            // -M: do not match; -a so unmatched are printed to stdout.
            var (code, outText, _) = Run("-M", src);

            // -M sets showUnmatched, so all refs print to stdout (as codes).
            Assert.Contains("DMC-EX-A-00-00-01-00A-040A-D_001-00", outText);
            Assert.Contains("PMC-EX-12345-00001-00_001-00", outText);
            Assert.Contains("COM-EX-12345-2026-00001-Q", outText);
            Assert.Contains("DML-EX-12345-C-2026-00001", outText);
            Assert.Contains("ABC-12345", outText);
            Assert.Contains("ICN-EX-A-000000-A-00001-A-001-01", outText);
            // With -M nothing can be matched: every ref counts as unmatched, so
            // the exit code reports unmatched references (mirrors the C tool).
            Assert.Equal(1, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TypeSelector_OnlyDm()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            var (_, outText, _) = Run("-M", "-D", src);

            Assert.Contains("DMC-EX-A-00-00-01-00A-040A-D_001-00", outText);
            Assert.DoesNotContain("PMC-", outText);
            Assert.DoesNotContain("COM-", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MatchesReferenceToFileInDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            // The matching target file for the referenced DM.
            string target = "DMC-EX-A-00-00-01-00A-040A-D_001-00_EN-CA.XML";
            WriteFixture(dir, target, "<dmodule/>");

            // Only list DM refs, match in the dir.
            var (code, outText, _) = Run("-D", "-d", dir, src);

            // The matched reference prints the full filename (with path prefix).
            Assert.Contains(target, outText);
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnmatchedReference_ReturnsExitCode1()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            // No target files present; matching is on.
            var (code, _, errText) = Run("-D", "-d", dir, src);

            Assert.Equal(1, code);
            Assert.Contains("Unmatched reference", errText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SourceColumn_PrependsFilename()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            string target = "DMC-EX-A-00-00-01-00A-040A-D_001-00_EN-CA.XML";
            WriteFixture(dir, target, "<dmodule/>");

            var (_, outText, _) = Run("-D", "-f", "-d", dir, src);

            Assert.Contains($"{src}: ", outText);
            Assert.Contains(target, outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnmatchedOnly_HidesMatched()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            string target = "DMC-EX-A-00-00-01-00A-040A-D_001-00_EN-CA.XML";
            WriteFixture(dir, target, "<dmodule/>");

            // -u (unmatched only) + -a (print unmatched to stdout). DM is matched
            // so it is hidden; PM/COM/etc. are unmatched and printed.
            var (_, outText, _) = Run("-u", "-a", "-d", dir, src);

            Assert.DoesNotContain(target, outText);
            Assert.Contains("PMC-EX-12345-00001-00_001-00", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IncludeSource_PrintsSourceObject()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            var (_, outText, _) = Run("-M", "-s", src);
            // The source path is printed as its own line first.
            Assert.StartsWith(src, outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ContentOnly_StillFindsContentRefs()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            var (_, outText, _) = Run("-M", "-c", "-D", src);
            Assert.Contains("DMC-EX-A-00-00-01-00A-040A-D_001-00", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IgnoreIssue_OmitsIssueInfoFromCode()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            var (_, outText, _) = Run("-M", "-i", "-D", src);
            // Without issue info the trailing _001-00 is dropped.
            Assert.Contains("DMC-EX-A-00-00-01-00A-040A-D", outText);
            Assert.DoesNotContain("DMC-EX-A-00-00-01-00A-040A-D_001-00", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomFormat_Expanded()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            var (_, outText, _) = Run("-M", "-D", "-t", "%code%", src);
            Assert.Contains("DMC-EX-A-00-00-01-00A-040A-D_001-00", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void XmlOutput_WrapsResults()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-refs-{Guid.NewGuid():N}");
        try
        {
            string src = WriteFixture(dir, "SRC.XML", ReferencingDm);
            // Matching is on; the DM ref has no target file, so it is reported as
            // <missing>. The C tool's XML report (matched and unmatched) is
            // written to stdout via printf.
            var (_, outText, _) = Run("-x", "-D", "-d", dir, src);
            Assert.Contains("<results>", outText);
            Assert.Contains("</results>", outText);
            Assert.Contains("<missing>", outText);
            Assert.Contains("<code>DMC-EX-A-00-00-01-00A-040A-D_001-00</code>", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Version_Prints522()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("5.2.2", outText);
    }

    [Fact]
    public void RegisteredInToolRegistry()
    {
        var tool = ToolRegistry.Resolve("refs");
        Assert.NotNull(tool);
        Assert.IsType<RefsTool>(tool);
    }
}
