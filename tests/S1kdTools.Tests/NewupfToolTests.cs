using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewupfToolTests
{
    private static (int code, string outText, string errText) Run(
        string workingDir, params string[] args)
    {
        // The tool resolves output paths relative to the current directory, so
        // run with the CWD set to the test's temp directory.
        string prev = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workingDir);
        try
        {
            var tool = new NewupfTool();
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-upf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Source CIR: issue 001, parts ABC, DEF, GHI.
    private const string SourceCir =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_4-2/xml_schema_flat/comrep.xsd">
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="00G" infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
                <issueInfo issueNumber="001" inWork="00"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2018" month="03" day="31"/>
                <dmTitle><techName>Example</techName><infoName>Parts common information repository</infoName></dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
              <responsiblePartnerCompany><enterpriseName>khzae.net</enterpriseName></responsiblePartnerCompany>
              <originator><enterpriseName>khzae.net</enterpriseName></originator>
              <applic><displayText><simplePara>All</simplePara></displayText></applic>
              <brexDmRef><dmRef><dmRefIdent><dmCode modelIdentCode="S1000D" systemDiffCode="F" systemCode="04" subSystemCode="1" subSubSystemCode="0" assyCode="0301" disassyCode="00" disassyCodeVariant="A" infoCode="022" infoCodeVariant="A" itemLocationCode="D"/></dmRefIdent></dmRef></brexDmRef>
              <qualityAssurance><unverified/></qualityAssurance>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <commonRepository>
              <partRepository>
                <partSpec><partIdent partNumberValue="ABC" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>ABC part</descrForPart></itemIdentData></partSpec>
                <partSpec><partIdent partNumberValue="DEF" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>DEF part</descrForPart></itemIdentData></partSpec>
                <partSpec><partIdent partNumberValue="GHI" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>GHI part</descrForPart></itemIdentData></partSpec>
              </partRepository>
            </commonRepository>
          </content>
        </dmodule>
        """;

    // Target CIR: issue 002. GHI deleted, JKL inserted (after DEF), DEF changed.
    private const string TargetCir =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_4-2/xml_schema_flat/comrep.xsd">
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="00G" infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
                <issueInfo issueNumber="002" inWork="00"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2018" month="03" day="31"/>
                <dmTitle><techName>Example</techName><infoName>Parts common information repository</infoName></dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="changed">
              <security securityClassification="01"/>
              <responsiblePartnerCompany><enterpriseName>khzae.net</enterpriseName></responsiblePartnerCompany>
              <originator><enterpriseName>khzae.net</enterpriseName></originator>
              <applic><displayText><simplePara>All</simplePara></displayText></applic>
              <brexDmRef><dmRef><dmRefIdent><dmCode modelIdentCode="S1000D" systemDiffCode="F" systemCode="04" subSystemCode="1" subSubSystemCode="0" assyCode="0301" disassyCode="00" disassyCodeVariant="A" infoCode="022" infoCodeVariant="A" itemLocationCode="D"/></dmRefIdent></dmRef></brexDmRef>
              <qualityAssurance><unverified/></qualityAssurance>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <commonRepository>
              <partRepository>
                <partSpec><partIdent partNumberValue="ABC" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>ABC part</descrForPart></itemIdentData></partSpec>
                <partSpec><partIdent partNumberValue="DEF" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>DEF part</descrForPart><shortName>DEF</shortName></itemIdentData></partSpec>
                <partSpec><partIdent partNumberValue="JKL" manufacturerCodeValue="12345"/><itemIdentData><descrForPart>JKL part</descrForPart></itemIdentData></partSpec>
              </partRepository>
            </commonRepository>
          </content>
        </dmodule>
        """;

    private static (string dir, string src, string tgt) WriteCirs()
    {
        string dir = NewTempDir();
        string src = Path.Combine(dir, "src.xml");
        string tgt = Path.Combine(dir, "tgt.xml");
        File.WriteAllText(src, SourceCir);
        File.WriteAllText(tgt, TargetCir);
        return (dir, src, tgt);
    }

    private const string ExpectedName =
        "UPF-EX-A-00-00-00-00A-00GA-D_001-00_EN-CA.XML";

    [Fact]
    public void GeneratesUpfWithAutomaticName()
    {
        var (dir, src, tgt) = WriteCirs();
        try
        {
            var (code, _, err) = Run(dir, "-v", src, tgt);
            Assert.Equal(0, code);

            string expected = Path.Combine(dir, ExpectedName);
            Assert.True(File.Exists(expected), $"expected {ExpectedName}; stderr={err}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UpfMetadataIsCorrect()
    {
        var (dir, src, tgt) = WriteCirs();
        try
        {
            string outPath = Path.Combine(dir, "out.xml");
            var (code, _, err) = Run(dir, "-f", "-@", outPath, src, tgt);
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath), err);

            var doc = XmlUtils.ReadDoc(outPath);

            // Root is a dataUpdateFile.
            Assert.Equal("dataUpdateFile", doc.DocumentElement!.Name);

            // updateCode has objectIdentCode UPF and the source code values.
            var updateCode = (XmlElement)doc.SelectSingleNode("//updateIdent/updateCode")!;
            Assert.Equal("UPF", updateCode.GetAttribute("objectIdentCode"));
            Assert.Equal("EX", updateCode.GetAttribute("modelIdentCode"));

            // sourceDmIdent / targetDmIssueInfo carry the two issues.
            var sourceDmIdent = (XmlElement)doc.SelectSingleNode("//updateStatus/sourceDmIdent")!;
            Assert.Equal("001",
                ((XmlElement)sourceDmIdent.SelectSingleNode("issueInfo")!).GetAttribute("issueNumber"));

            var targetDmIssueInfo = (XmlElement)doc.SelectSingleNode("//updateStatus/targetDmIssueInfo")!;
            Assert.Equal("002", targetDmIssueInfo.GetAttribute("issueNumber"));

            // targetDmStatus copied from target's dmStatus (issueType="changed").
            var targetDmStatus = (XmlElement)doc.SelectSingleNode("//updateIdentAndStatusSection/targetDmStatus")!;
            Assert.Equal("changed", targetDmStatus.GetAttribute("issueType"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DiffsDeleteInsertReplace()
    {
        var (dir, src, tgt) = WriteCirs();
        try
        {
            string outPath = Path.Combine(dir, "out.xml");
            var (code, _, _) = Run(dir, "-f", "-@", outPath, src, tgt);
            Assert.Equal(0, code);

            var doc = XmlUtils.ReadDoc(outPath);

            // GHI present in source but not target -> deleteObject.
            var deleted = doc.SelectSingleNode(
                "//deleteObjectGroup/deleteObject/partIdent[@partNumberValue='GHI']");
            Assert.NotNull(deleted);

            // JKL present in target but not source -> insertObject (after DEF).
            var insert = (XmlElement)doc.SelectSingleNode(
                "//insertObjectGroup/insertObject[partSpec/partIdent/@partNumberValue='JKL']")!;
            Assert.NotNull(insert);
            Assert.Equal("after", insert.GetAttribute("insertionOrder"));
            Assert.Contains("partNumberValue='DEF'", insert.GetAttribute("targetPath"));

            // DEF differs between issues -> replaceObject carrying the target's DEF.
            var replace = doc.SelectSingleNode(
                "//replaceObjectGroup/replaceObject/partSpec[partIdent/@partNumberValue='DEF']/itemIdentData/shortName");
            Assert.NotNull(replace);

            // ABC unchanged -> appears in none of the groups.
            Assert.Null(doc.SelectSingleNode("//deleteObject//*[@partNumberValue='ABC']"));
            Assert.Null(doc.SelectSingleNode("//insertObject//*[@partNumberValue='ABC']"));
            Assert.Null(doc.SelectSingleNode("//replaceObject//*[@partNumberValue='ABC']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DifferentCirTypesIsInvalid()
    {
        var (dir, src, _) = WriteCirs();
        try
        {
            // Target with a different repository type.
            string tgt = Path.Combine(dir, "tgt2.xml");
            File.WriteAllText(tgt, TargetCir.Replace("partRepository", "toolRepository"));

            var (code, _, _) = Run(dir, "-f", "-@", Path.Combine(dir, "o.xml"), src, tgt);
            Assert.Equal(3, code); // EXIT_INVALID_ARGS
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingArgsReturnsTwo()
    {
        var (dir, src, _) = WriteCirs();
        try
        {
            var (code, _, _) = Run(dir, src); // only one positional
            Assert.Equal(2, code); // EXIT_MISSING_ARGS
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFileWithoutOverwriteFails()
    {
        var (dir, src, tgt) = WriteCirs();
        try
        {
            string expected = Path.Combine(dir, ExpectedName);
            File.WriteAllText(expected, "<x/>");

            var (code, _, _) = Run(dir, src, tgt);
            Assert.Equal(1, code); // EXIT_UPF_EXISTS

            // -q suppresses the error and returns 0.
            var (qcode, _, _) = Run(dir, "-q", src, tgt);
            Assert.Equal(0, qcode);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Issue42ConversionChangesSchemaLocation()
    {
        var (dir, src, tgt) = WriteCirs();
        try
        {
            string outPath = Path.Combine(dir, "out.xml");
            var (code, _, err) = Run(dir, "-f", "-$", "4.2", "-@", outPath, src, tgt);
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath), err);

            string text = File.ReadAllText(outPath);
            Assert.Contains("S1000D_4-2", text);
            Assert.DoesNotContain("S1000D_6/", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DumpTemplatesWritesUpdateXml()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir, "-~", dir);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "update.xml")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
