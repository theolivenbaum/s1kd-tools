using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class UpissueToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new UpissueTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempName(string baseName)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-up-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, baseName);
    }

    private static XmlElement IssueInfo(string path)
    {
        var doc = XmlUtils.ReadDoc(path);
        return (XmlElement)doc.SelectSingleNode("//issueInfo")!;
    }

    private const string DmName = "DMC-EX-A-00-00-00-00A-040A-D_002-01_EN-CA.XML";

    // --------------------------------------------------------------------

    [Fact]
    public void Inwork_Increment_RenamesFileAndUpdatesMetadata()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-f", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
            Assert.True(File.Exists(expected), "renamed file should exist");

            var ii = IssueInfo(expected);
            Assert.Equal("002", ii.GetAttribute("issueNumber"));
            Assert.Equal("02", ii.GetAttribute("inWork"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Official_Increment_BumpsIssueResetsInwork()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-i", "-f", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_003-00_EN-CA.XML");
            Assert.True(File.Exists(expected));

            var ii = IssueInfo(expected);
            Assert.Equal("003", ii.GetAttribute("issueNumber"));
            Assert.Equal("00", ii.GetAttribute("inWork"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void OfficialToInwork_ResetsQaToUnverified_AndDeletesRfus()
    {
        // Start from an official (inWork="00") module with QA + an RFU.
        const string xml =
            """
            <dmodule>
              <identAndStatusSection>
                <dmAddress>
                  <dmIdent>
                    <issueInfo issueNumber="001" inWork="00"/>
                  </dmIdent>
                  <dmAddressItems>
                    <issueDate year="2020" month="01" day="01"/>
                  </dmAddressItems>
                </dmAddress>
                <dmStatus issueType="status">
                  <qualityAssurance><firstVerification verificationType="tabtop"/></qualityAssurance>
                  <reasonForUpdate id="rfu-1"><simplePara>Initial</simplePara></reasonForUpdate>
                </dmStatus>
              </identAndStatusSection>
              <content><para changeType="modify" reasonForUpdateRefIds="rfu-1">Hi</para></content>
            </dmodule>
            """;
        string path = TempName("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML");
        File.WriteAllText(path, xml);
        try
        {
            var (code, _, _) = Run("-f", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(expected);

            // QA should now be unverified.
            Assert.NotNull(doc.SelectSingleNode("//qualityAssurance/unverified"));
            Assert.Null(doc.SelectSingleNode("//firstVerification"));
            // RFU should be deleted.
            Assert.Null(doc.SelectSingleNode("//reasonForUpdate"));
            // Change-mark attributes stripped from content.
            var para = (XmlElement)doc.SelectSingleNode("//para")!;
            Assert.False(para.HasAttribute("changeType"));
            Assert.False(para.HasAttribute("reasonForUpdateRefIds"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void AddReason_AppendsRfuToContent()
    {
        const string xml =
            """
            <dmodule>
              <identAndStatusSection>
                <dmAddress><dmIdent>
                  <issueInfo issueNumber="001" inWork="01"/>
                </dmIdent>
                <dmAddressItems><issueDate year="2020" month="01" day="01"/></dmAddressItems>
                </dmAddress>
                <dmStatus issueType="changed">
                  <qualityAssurance><unverified/></qualityAssurance>
                </dmStatus>
              </identAndStatusSection>
              <content><para>Hi</para></content>
            </dmodule>
            """;
        string path = TempName("DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML");
        File.WriteAllText(path, xml);
        try
        {
            var (code, _, _) = Run("-c", "Some reason", "-f", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_001-02_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(expected);
            var rfu = doc.SelectSingleNode("//reasonForUpdate");
            Assert.NotNull(rfu);
            Assert.Equal("Some reason", doc.SelectSingleNode("//reasonForUpdate/simplePara")!.InnerText);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void OmitIssue_OverwritesInPlace_NoRename()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-N", path);
            Assert.Equal(0, code);

            // File keeps its original name (no rename with -N).
            Assert.True(File.Exists(path));
            var ii = IssueInfo(path);
            Assert.Equal("002", ii.GetAttribute("issueNumber"));
            Assert.Equal("02", ii.GetAttribute("inWork"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void PrintFilenames_EmitsNewName()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, outText, _) = Run("-5", "-f", path);
            Assert.Equal(0, code);
            Assert.Contains("_002-02_", outText);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void DryRun_DoesNotCreateOrModifyFiles()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-d", "-5", path);
            Assert.Equal(0, code);

            // No new file created.
            string renamed = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
            Assert.False(File.Exists(renamed));
            // Original unchanged.
            var ii = IssueInfo(path);
            Assert.Equal("002", ii.GetAttribute("issueNumber"));
            Assert.Equal("01", ii.GetAttribute("inWork"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Erase_RemovesOldIssue()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-e", "-f", path);
            Assert.Equal(0, code);
            Assert.False(File.Exists(path), "old issue should be erased");
            string renamed = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
            Assert.True(File.Exists(renamed));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void IssueType_OverridesStatus()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-z", "rinstate-changed", "-f", path);
            Assert.Equal(0, code);
            string renamed = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(renamed);
            var dmStatus = (XmlElement)doc.SelectSingleNode("//dmStatus")!;
            Assert.Equal("rinstate-changed", dmStatus.GetAttribute("issueType"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Modify_NoUpissue_WritesToStdoutByDefault()
    {
        // add_rfus needs an existing qualityAssurance/reasonForUpdate anchor to
        // attach the new RFU after, so use a fixture that includes QA.
        const string xml =
            """
            <dmodule>
              <identAndStatusSection>
                <dmAddress><dmIdent>
                  <issueInfo issueNumber="002" inWork="01"/>
                </dmIdent>
                <dmAddressItems><issueDate year="2020" month="01" day="01"/></dmAddressItems>
                </dmAddress>
                <dmStatus issueType="changed">
                  <qualityAssurance><unverified/></qualityAssurance>
                </dmStatus>
              </identAndStatusSection>
              <content><para>Hi</para></content>
            </dmodule>
            """;
        string path = TempName(DmName);
        File.WriteAllText(path, xml);
        try
        {
            var (code, outText, _) = Run("-m", "-c", "Reason", path);
            Assert.Equal(0, code);
            // Issue/inwork unchanged in the file (modify-only) ...
            var ii = IssueInfo(path);
            Assert.Equal("002", ii.GetAttribute("issueNumber"));
            Assert.Equal("01", ii.GetAttribute("inWork"));
            // ... and the modified doc went to stdout with the RFU added.
            Assert.Contains("reasonForUpdate", outText);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void MissingFile_ReturnsExit1()
    {
        var (code, _, err) = Run("-f", "/no/such/file_001-01_EN.XML");
        Assert.Equal(1, code);
        Assert.Contains("Could not read file", err);
    }

    [Fact]
    public void BadFilename_NoIssueInfo_ReturnsExit3()
    {
        // Non-XML content => dmdoc is null, no issueInfo. The name passed to the
        // tool must contain no '_' and no '-' to reach the bad-filename branch
        // (the C inspects the whole path with strchr). Run from a dash-free
        // working directory and pass a plain basename.
        string dir = Path.Combine(Path.GetTempPath(), $"s1kdup{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string original = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "plainfile.txt"), "not xml");
            Directory.SetCurrentDirectory(dir);

            var (code, _, err) = Run("-f", "plainfile.txt");
            Assert.Equal(3, code);
            Assert.Contains("does not contain issue info", err);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IcnWithoutOfficial_ReturnsExit5()
    {
        // ICN (non-XML, name with '-' but no '_'): inwork upissue not allowed.
        string path = TempName("ICN-EX-A-000000-A-001-01.JPG");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF });
        try
        {
            var (code, _, err) = Run("-f", path);
            Assert.Equal(5, code);
            Assert.Contains("ICNs cannot have inwork", err);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void MaxIssue_ReturnsExit6()
    {
        const string xml =
            """
            <dmodule>
              <identAndStatusSection><dmAddress><dmIdent>
                <issueInfo issueNumber="999" inWork="00"/>
              </dmIdent></dmAddress></identAndStatusSection>
            </dmodule>
            """;
        string path = TempName("DMC-EX-A-00-00-00-00A-040A-D_999-00_EN-CA.XML");
        File.WriteAllText(path, xml);
        try
        {
            var (code, _, err) = Run("-i", "-f", path);
            Assert.Equal(6, code);
            Assert.Contains("max issue number", err);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void NoOverwrite_WhenTargetExists_ReturnsExit2()
    {
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        string target = Path.Combine(Path.GetDirectoryName(path)!,
            "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
        File.WriteAllText(target, "<existing/>");
        try
        {
            var (code, _, err) = Run(path); // no -f
            Assert.Equal(2, code);
            Assert.Contains("already exists", err);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Help_And_Version()
    {
        var (hc, hOut, _) = Run("-h");
        Assert.Equal(0, hc);
        Assert.Contains("Usage: s1kd-upissue", hOut);

        var (vc, vOut, _) = Run("--version");
        Assert.Equal(0, vc);
        Assert.Contains("5.0.1", vOut);
    }

    // ---- option-parsing fidelity -----------------------------------------

    [Fact]
    public void BundledShortFlags_AreSplit()
    {
        // -ife == -i -f -e : official upissue (003-00), overwrite, erase old.
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, _) = Run("-ife", path);
            Assert.Equal(0, code);

            // -e erases the old file; -i bumps issue and resets inwork.
            Assert.False(File.Exists(path), "old issue erased by bundled -e");
            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_003-00_EN-CA.XML");
            Assert.True(File.Exists(expected));

            var ii = IssueInfo(expected);
            Assert.Equal("003", ii.GetAttribute("issueNumber"));
            Assert.Equal("00", ii.GetAttribute("inWork"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void BundledShortFlags_ArgTakingFlagConsumesRestOfToken()
    {
        // -fc"Reason" : -f overwrite, then -c takes "Reason" from the rest.
        const string xml =
            """
            <dmodule>
              <identAndStatusSection>
                <dmAddress><dmIdent>
                  <issueInfo issueNumber="001" inWork="01"/>
                </dmIdent>
                <dmAddressItems><issueDate year="2020" month="01" day="01"/></dmAddressItems>
                </dmAddress>
                <dmStatus issueType="changed">
                  <qualityAssurance><unverified/></qualityAssurance>
                </dmStatus>
              </identAndStatusSection>
              <content><para>Hi</para></content>
            </dmodule>
            """;
        string path = TempName("DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML");
        File.WriteAllText(path, xml);
        try
        {
            var (code, _, _) = Run("-fcReason", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_001-02_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(expected);
            Assert.Equal("Reason", doc.SelectSingleNode("//reasonForUpdate/simplePara")!.InnerText);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void BundledShortFlags_ArgTakingFlagAtEndTakesNextArg()
    {
        // -fc Reason : -c at end of bundle pulls the next argument.
        const string xml =
            """
            <dmodule>
              <identAndStatusSection>
                <dmAddress><dmIdent>
                  <issueInfo issueNumber="001" inWork="01"/>
                </dmIdent>
                <dmAddressItems><issueDate year="2020" month="01" day="01"/></dmAddressItems>
                </dmAddress>
                <dmStatus issueType="changed">
                  <qualityAssurance><unverified/></qualityAssurance>
                </dmStatus>
              </identAndStatusSection>
              <content><para>Hi</para></content>
            </dmodule>
            """;
        string path = TempName("DMC-EX-A-00-00-00-00A-040A-D_001-01_EN-CA.XML");
        File.WriteAllText(path, xml);
        try
        {
            var (code, _, _) = Run("-fc", "Next reason", path);
            Assert.Equal(0, code);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_001-02_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(expected);
            Assert.Equal("Next reason", doc.SelectSingleNode("//reasonForUpdate/simplePara")!.InnerText);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Libxml2LongOpts_AreAcceptedNotRejected()
    {
        // The libxml2 parser long-options must be accepted (getopt_long table),
        // not treated as unknown options. --xml-catalog takes an argument.
        string path = TempName(DmName);
        File.WriteAllText(path, Fixtures.DataModule);
        try
        {
            var (code, _, err) = Run("--huge", "--net", "--noent", "--xinclude",
                "--parser-errors", "--parser-warnings", "--dtdload",
                "--xml-catalog", "/tmp/none.xml", "-f", path);
            Assert.Equal(0, code);
            Assert.DoesNotContain("Unknown option", err);

            string expected = Path.Combine(Path.GetDirectoryName(path)!,
                "DMC-EX-A-00-00-00-00A-040A-D_002-02_EN-CA.XML");
            Assert.True(File.Exists(expected));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Help_ListsXmlParserOptions()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("XML parser options:", outText);
        Assert.Contains("--xml-catalog", outText);
        Assert.Contains("--huge", outText);
    }

    [Fact]
    public void UnknownShortFlag_InBundle_ReturnsExit2()
    {
        var (code, _, err) = Run("-fX", "file_001-01_EN.XML");
        Assert.Equal(2, code);
        Assert.Contains("Unknown option: -X", err);
    }
}
