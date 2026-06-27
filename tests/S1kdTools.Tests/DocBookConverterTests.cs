using System.Xml;
using S1kdTools.DocBook;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class DocBookConverterTests
{
    private const string DocBookNs = "http://docbook.org/ns/docbook";

    // A self-contained data module with an internal DTD subset declaring an
    // unparsed (NDATA) ICN entity, referenced from a figure's graphic — this is
    // what exercises the EntityUriResolver shim that replaces unparsed-entity-uri().
    private const string DmWithGraphic =
        """
        <?xml version="1.0"?>
        <!DOCTYPE dmodule [
        <!NOTATION PNG SYSTEM "PNG">
        <!ENTITY ICN-TEST-00001-001-01 SYSTEM "ICN-TEST-00001-001-01.PNG" NDATA PNG>
        ]>
        <dmodule xmlns:xlink="http://www.w3.org/1999/xlink"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_4-2/xml_schema_flat/descript.xsd">
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00" subSystemCode="0"
                        subSubSystemCode="0" assyCode="0000" disassyCode="00" disassyCodeVariant="A"
                        infoCode="040" infoCodeVariant="A" itemLocationCode="A"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
                <issueInfo issueNumber="001" inWork="00"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2026" month="06" day="27"/>
                <dmTitle><techName>Widget</techName><infoName>Description</infoName></dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
              <responsiblePartnerCompany><enterpriseName>Test Co</enterpriseName></responsiblePartnerCompany>
              <originator><enterpriseName>Test Co</enterpriseName></originator>
              <applicCrossRefTableRef/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <description>
              <levelledPara>
                <title>Scope</title>
                <para>Hello world.</para>
                <figure id="fig-0001">
                  <title>A figure</title>
                  <graphic infoEntityIdent="ICN-TEST-00001-001-01"/>
                </figure>
              </levelledPara>
            </description>
          </content>
        </dmodule>
        """;

    [Theory]
    [InlineData(DocBookProfile.S1kd2db)]
    [InlineData(DocBookProfile.SmartAvionics)]
    public void Convert_ProducesWellFormedDocBook5(DocBookProfile profile)
    {
        string result = DocBookConverter.Convert(DmWithGraphic, profile);

        var doc = new XmlDocument();
        doc.LoadXml(result); // throws if not well-formed (and asserts no BOM)

        // The DocBook namespace appears (as default ns or on the root element).
        Assert.Contains(DocBookNs, result);
        Assert.DoesNotContain('﻿', result); // no byte-order mark, like xsltproc
    }

    [Theory]
    [InlineData(DocBookProfile.S1kd2db)]
    [InlineData(DocBookProfile.SmartAvionics)]
    public void Convert_ResolvesGraphicEntityToSystemId(DocBookProfile profile)
    {
        string result = DocBookConverter.Convert(DmWithGraphic, profile);

        // unparsed-entity-uri()/ier:resolve() must turn the entity name into its
        // NDATA system id (the .PNG file), not leave the bare entity name.
        Assert.Contains("fileref=\"ICN-TEST-00001-001-01.PNG\"", result);
    }

    [Fact]
    public void Convert_TwoProfiles_DifferInCoverage()
    {
        string basic = DocBookConverter.Convert(DmWithGraphic, DocBookProfile.S1kd2db);
        string smart = DocBookConverter.Convert(DmWithGraphic, DocBookProfile.SmartAvionics);

        // Different stylesheet sets → different serializations (root element
        // differs: s1kd2db emits <article>, s1000dtodb emits <book>).
        Assert.NotEqual(basic, smart);
    }

    [Fact]
    public void Convert_PassesStylesheetParameters()
    {
        // info.name.is.subtitle makes dmTitle/infoName a DocBook subtitle.
        var withSubtitle = new Dictionary<string, string> { ["info.name.is.subtitle"] = "1" };
        string result = DocBookConverter.Convert(DmWithGraphic, DocBookProfile.S1kd2db, withSubtitle);

        Assert.Contains("subtitle", result);
        Assert.Contains("Description", result);
    }

    [Fact]
    public void ReadUnparsedEntities_FindsNdataEntities()
    {
        var doc = new XmlDocument { XmlResolver = null };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using (XmlReader r = XmlReader.Create(new StringReader(DmWithGraphic), settings))
            doc.Load(r);

        var map = EntityUriResolver.ReadUnparsedEntities(doc);

        Assert.True(map.ContainsKey("ICN-TEST-00001-001-01"));
        Assert.Equal("ICN-TEST-00001-001-01.PNG", map["ICN-TEST-00001-001-01"]);
    }

    [Fact]
    public void ReadInfoEntityMap_ParsesPropertiesFile()
    {
        string text = "# comment\nICN-0001=graphics/one.png\n! bang comment\nICN-0002 : graphics/two.svg\n\n";
        var map = EntityUriResolver.ReadInfoEntityMap(text);

        Assert.Equal(2, map.Count);
        Assert.Equal("graphics/one.png", map["ICN-0001"]);
        Assert.Equal("graphics/two.svg", map["ICN-0002"]);
    }

    [Fact]
    public void InfoEntityMap_OverridesDtdEntity()
    {
        var infoMap = new Dictionary<string, string> { ["ICN-TEST-00001-001-01"] = "custom/path.tif" };
        string result = DocBookConverter.Convert(DmWithGraphic, DocBookProfile.S1kd2db, parameters: null, infoEntityMap: infoMap);

        Assert.Contains("fileref=\"custom/path.tif\"", result);
    }

    // ----- the CLI tool wrapper -----

    private static (int code, string outText, string errText) RunTool(string stdin, params string[] args)
    {
        var tool = new S1kd2dbTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        TextReader savedIn = Console.In;
        try
        {
            Console.SetIn(new StringReader(stdin));
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetIn(savedIn);
        }
    }

    [Fact]
    public void Tool_ConvertsStdin_DefaultProfile()
    {
        var (code, outText, _) = RunTool(DmWithGraphic);
        Assert.Equal(0, code);
        Assert.Contains(DocBookNs, outText);
        Assert.Contains("<d:article", outText); // s1kd2db default
    }

    [Fact]
    public void Tool_SmartProfile_UsesS1000dtodb()
    {
        var (code, outText, _) = RunTool(DmWithGraphic, "-S");
        Assert.Equal(0, code);
        Assert.Contains("<book", outText); // s1000dtodb emits <book>
    }

    [Fact]
    public void Tool_UnknownOption_ReturnsTwo()
    {
        var (code, _, errText) = RunTool(DmWithGraphic, "--bogus");
        Assert.Equal(2, code);
        Assert.Contains("Unknown option", errText);
    }

    [Fact]
    public void Tool_IsRegisteredInRegistry()
    {
        Assert.NotNull(ToolRegistry.Resolve("s1kd2db"));
        Assert.NotNull(ToolRegistry.Resolve("s1kd-s1kd2db"));
    }
}
