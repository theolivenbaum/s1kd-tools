using System.Xml;
using S1kdTools;

namespace S1kdTools.Tests;

public class MetadataTests
{
    [Theory]
    [InlineData("type", "dmodule")]
    [InlineData("techName", "Example")]
    [InlineData("infoName", "Description")]
    [InlineData("issueNumber", "002")]
    [InlineData("inWork", "01")]
    [InlineData("issueInfo", "002-01")]
    [InlineData("securityClassification", "01")]
    [InlineData("issueType", "changed")]
    [InlineData("language", "en-CA")]
    [InlineData("modelIdentCode", "EX")]
    [InlineData("infoCode", "040")]
    [InlineData("responsiblePartnerCompany", "Example Company")]
    [InlineData("originatorCode", "ABCDE")]
    public void Get_ReturnsExpected(string key, string expected)
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal(expected, Metadata.Get(doc, key));
    }

    [Fact]
    public void Get_MissingMetadata_ReturnsNull()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Null(Metadata.Get(doc, "pmTitle"));
    }

    [Fact]
    public void Set_TechName_Updates()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "techName", "Changed Name"));
        Assert.Equal("Changed Name", Metadata.Get(doc, "techName"));
    }

    [Fact]
    public void Set_IssueNumber_UpdatesAttribute()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "issueNumber", "003"));
        Assert.Equal("003", Metadata.Get(doc, "issueNumber"));
        Assert.Equal("003-01", Metadata.Get(doc, "issueInfo"));
    }

    [Fact]
    public void Set_SecurityClassification_UpdatesAttribute()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "securityClassification", "02"));
        Assert.Equal("02", Metadata.Get(doc, "securityClassification"));
    }

    [Fact]
    public void Set_NonEditableKey_ReturnsFalse()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.False(Metadata.Set(doc, "type", "whatever"));
    }

    [Fact]
    public void AllKeys_HaveUniqueNames()
    {
        var dupes = Metadata.Keys.GroupBy(k => k.Name).Where(g => g.Count() > 1).ToList();
        Assert.Empty(dupes);
    }

    // ---------------------------------------------------------------------
    // New key coverage
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("act")]
    [InlineData("issue")]
    [InlineData("issueDate")]
    [InlineData("qualityAssurance")]
    [InlineData("source")]
    [InlineData("sourceDmCode")]
    [InlineData("sourcePmCode")]
    [InlineData("sourceIssueNumber")]
    [InlineData("sourceInWork")]
    [InlineData("sourceLanguageIsoCode")]
    [InlineData("sourceCountryIsoCode")]
    [InlineData("learnCode")]
    [InlineData("learnEventCode")]
    [InlineData("skillLevelCode")]
    [InlineData("firstVerificationType")]
    [InlineData("secondVerificationType")]
    [InlineData("commentPriority")]
    [InlineData("commentResponse")]
    [InlineData("shortPmTitle")]
    public void Keys_ContainsNewlyAddedKey(string name)
    {
        Assert.True(Metadata.IsKnown(name), $"Expected key {name} to be known");
    }

    [Fact]
    public void Keys_CountMatchesPortedTable()
    {
        // The C metadata[] has 75 distinct keys; "format", "modified" and "path"
        // are file-system metadata derived outside the XML and are excluded here.
        Assert.Equal(72, Metadata.Keys.Count);
    }

    [Fact]
    public void Keys_EditableFlagMatchesC()
    {
        bool Editable(string n) => Metadata.Keys.Single(k => k.Name == n).Editable;

        // Has an edit function in the C => editable.
        Assert.True(Editable("dmCode"));
        Assert.True(Editable("pmCode"));
        Assert.True(Editable("code"));
        Assert.True(Editable("issueDate"));
        // Has only a create function => still editable.
        Assert.True(Editable("skillLevelCode"));
        // No edit nor create function => not editable.
        Assert.False(Editable("type"));
        Assert.False(Editable("schema"));
        Assert.False(Editable("issueInfo"));
        Assert.False(Editable("language"));
        Assert.False(Editable("commentCode"));
        Assert.False(Editable("ddnCode"));
        Assert.False(Editable("dmlCode"));
        Assert.False(Editable("qualityAssurance"));
        Assert.False(Editable("source"));
        Assert.False(Editable("title"));
    }

    // ---------------------------------------------------------------------
    // Composite getters
    // ---------------------------------------------------------------------

    [Fact]
    public void Get_DmCode_AssemblesFullCode()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal("EX-A-00-00-00-00A-040A-D", Metadata.Get(doc, "dmCode"));
    }

    [Fact]
    public void Get_Code_DispatchesToDmCode()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal("EX-A-00-00-00-00A-040A-D", Metadata.Get(doc, "code"));
    }

    [Fact]
    public void Get_Title_CombinesTechAndInfoName()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal("Example - Description", Metadata.Get(doc, "title"));
    }

    [Fact]
    public void Get_QualityAssurance_Unverified()
    {
        const string xml = "<qualityAssurance><unverified/></qualityAssurance>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("unverified", Metadata.Get(doc, "qualityAssurance"));
    }

    [Fact]
    public void Get_QualityAssurance_FirstVerification()
    {
        const string xml =
            "<qualityAssurance><firstVerification verificationType=\"tabtop\"/></qualityAssurance>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("firstVerification", Metadata.Get(doc, "qualityAssurance"));
        Assert.Equal("tabtop", Metadata.Get(doc, "firstVerificationType"));
    }

    [Fact]
    public void Get_IssueDate_FormatsYmd()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal("2026-06-25", Metadata.Get(doc, "issueDate"));
    }

    [Fact]
    public void Get_PmCode_AssemblesFromPmCodeElement()
    {
        const string xml =
            "<pm><pmAddress><pmIdent>" +
            "<pmCode modelIdentCode=\"EX\" pmIssuer=\"12345\" pmNumber=\"00000\" pmVolume=\"00\"/>" +
            "</pmIdent></pmAddress></pm>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("EX-12345-00000-00", Metadata.Get(doc, "pmCode"));
        Assert.Equal("EX-12345-00000-00", Metadata.Get(doc, "code"));
        Assert.Equal("12345", Metadata.Get(doc, "pmIssuer"));
    }

    [Fact]
    public void Get_Source_FullSourceIdentification()
    {
        const string xml =
            "<dmodule><sourceDmIdent>" +
            "<dmCode modelIdentCode=\"EX\" systemDiffCode=\"A\" systemCode=\"00\" subSystemCode=\"0\" " +
            "subSubSystemCode=\"0\" assyCode=\"00\" disassyCode=\"00\" disassyCodeVariant=\"A\" " +
            "infoCode=\"040\" infoCodeVariant=\"A\" itemLocationCode=\"D\"/>" +
            "<issueInfo issueNumber=\"001\" inWork=\"00\"/>" +
            "<language languageIsoCode=\"en\" countryIsoCode=\"CA\"/>" +
            "</sourceDmIdent></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("DMC-EX-A-00-00-00-00A-040A-D_001-00_en-CA", Metadata.Get(doc, "source"));
    }

    // ---------------------------------------------------------------------
    // Composite / dispatching setters
    // ---------------------------------------------------------------------

    [Fact]
    public void Set_DmCode_RewritesAllAttributes()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "dmCode", "AB-B-11-22-33-44A-555B-C"));
        Assert.Equal("AB-B-11-22-33-44A-555B-C", Metadata.Get(doc, "dmCode"));
        Assert.Equal("AB", Metadata.Get(doc, "modelIdentCode"));
        Assert.Equal("11", Metadata.Get(doc, "systemCode"));
    }

    [Fact]
    public void Set_DmCode_WithLearnCode()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "dmCode", "EX-A-00-00-00-00A-040A-D-H10A"));
        Assert.Equal("H10", Metadata.Get(doc, "learnCode"));
        Assert.Equal("A", Metadata.Get(doc, "learnEventCode"));
        Assert.Equal("EX-A-00-00-00-00A-040A-D-H10A", Metadata.Get(doc, "dmCode"));
    }

    [Fact]
    public void Set_DmCode_AcceptsDmcPrefix()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "dmCode", "DMC-AB-B-11-22-33-44A-555B-C"));
        Assert.Equal("AB-B-11-22-33-44A-555B-C", Metadata.Get(doc, "dmCode"));
    }

    [Fact]
    public void Set_DmCode_InvalidValue_ReturnsFalse()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.False(Metadata.Set(doc, "dmCode", "not-a-code"));
    }

    [Fact]
    public void Set_IssueDate_SetsAttributes()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "issueDate", "2030-01-02"));
        Assert.Equal("2030-01-02", Metadata.Get(doc, "issueDate"));
    }

    [Fact]
    public void Set_IssueDate_InvalidValue_ReturnsFalse()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.False(Metadata.Set(doc, "issueDate", "not-a-date"));
    }

    [Fact]
    public void Set_FirstVerificationType_Unverified_RemovesVerification()
    {
        const string xml =
            "<qualityAssurance><firstVerification verificationType=\"tabtop\"/></qualityAssurance>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.True(Metadata.Set(doc, "firstVerificationType", "unverified"));
        Assert.Equal("unverified", Metadata.Get(doc, "qualityAssurance"));
        Assert.NotNull(doc.SelectSingleNode("//unverified"));
    }

    [Fact]
    public void Set_SchemaUrl_AndIssue_RoundTrips()
    {
        const string xml =
            "<dmodule xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
            "xsi:noNamespaceSchemaLocation=\"http://www.s1000d.org/S1000D_4-1/xml_schema_flat/descript.xsd\"/>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("4.1", Metadata.Get(doc, "issue"));
        Assert.Equal("descript", Metadata.Get(doc, "schema"));

        Assert.True(Metadata.Set(doc, "issue", "5.0"));
        Assert.Equal("5.0", Metadata.Get(doc, "issue"));
    }

    // ---------------------------------------------------------------------
    // create-on-set
    // ---------------------------------------------------------------------

    [Fact]
    public void Set_InfoName_CreatesNodeAfterTechName()
    {
        const string xml =
            "<dmodule><dmTitle><techName>Example</techName></dmTitle></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "infoName"));
        Assert.True(Metadata.Set(doc, "infoName", "Created Name"));
        Assert.Equal("Created Name", Metadata.Get(doc, "infoName"));

        var title = doc.SelectSingleNode("//dmTitle")!;
        Assert.Equal("techName", title.ChildNodes[0]!.Name);
        Assert.Equal("infoName", title.ChildNodes[1]!.Name);
    }

    [Fact]
    public void Set_InfoName_EmptyValue_RemovesNode()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "infoName", ""));
        Assert.Null(doc.SelectSingleNode("//infoName"));
    }

    [Fact]
    public void Set_Originator_CreatesEnterpriseName()
    {
        const string xml =
            "<dmodule><dmStatus><originator enterpriseCode=\"ABCDE\"/></dmStatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "originator"));
        Assert.True(Metadata.Set(doc, "originator", "Created Co"));
        Assert.Equal("Created Co", Metadata.Get(doc, "originator"));
    }

    [Fact]
    public void Set_ResponsiblePartnerCompany_CreatesEnterpriseName()
    {
        const string xml =
            "<dmodule><dmStatus><responsiblePartnerCompany enterpriseCode=\"ABCDE\"/></dmStatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "responsiblePartnerCompany"));
        Assert.True(Metadata.Set(doc, "responsiblePartnerCompany", "Partner Co"));
        Assert.Equal("Partner Co", Metadata.Get(doc, "responsiblePartnerCompany"));
    }

    [Fact]
    public void Set_Remarks_CreatesRemarksWithSimplePara()
    {
        const string xml =
            "<dmodule><dmStatus><security securityClassification=\"01\"/></dmStatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "remarks"));
        Assert.True(Metadata.Set(doc, "remarks", "A remark"));
        Assert.Equal("A remark", Metadata.Get(doc, "remarks"));
        Assert.NotNull(doc.SelectSingleNode("//dmStatus/remarks/simplePara"));
    }

    [Fact]
    public void Set_ReasonForUpdate_CreatesNode()
    {
        const string xml =
            "<dmodule><dmStatus><security securityClassification=\"01\"/></dmStatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "reasonForUpdate"));
        Assert.True(Metadata.Set(doc, "reasonForUpdate", "Because"));
        Assert.Equal("Because", Metadata.Get(doc, "reasonForUpdate"));
        Assert.NotNull(doc.SelectSingleNode("//dmStatus/reasonForUpdate/simplePara"));
    }

    [Fact]
    public void Set_SkillLevel_CreatesNode()
    {
        const string xml =
            "<dmodule><dmStatus><qualityAssurance><unverified/></qualityAssurance></dmStatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Null(Metadata.Get(doc, "skillLevelCode"));
        Assert.True(Metadata.Set(doc, "skillLevelCode", "sk01"));
        Assert.Equal("sk01", Metadata.Get(doc, "skillLevelCode"));
    }

    [Fact]
    public void Set_FirstVerification_CreatesFromUnverified()
    {
        const string xml = "<qualityAssurance><unverified/></qualityAssurance>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.True(Metadata.Set(doc, "firstVerificationType", "tabtop"));
        Assert.Equal("tabtop", Metadata.Get(doc, "firstVerificationType"));
        Assert.Null(doc.SelectSingleNode("//unverified"));
    }

    [Fact]
    public void Set_SecondVerification_CreatesAfterFirst()
    {
        const string xml =
            "<qualityAssurance><firstVerification verificationType=\"tabtop\"/></qualityAssurance>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.True(Metadata.Set(doc, "secondVerificationType", "onobject"));
        Assert.Equal("onobject", Metadata.Get(doc, "secondVerificationType"));
        Assert.Equal("secondVerification", Metadata.Get(doc, "qualityAssurance"));
    }

    [Fact]
    public void Set_MissingNoCreate_ReturnsFalse()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        // pmTitle has no create function; node absent => false.
        Assert.False(Metadata.Set(doc, "pmTitle", "Whatever"));
    }

    // ---------------------------------------------------------------------
    // Legacy SGML forms
    // ---------------------------------------------------------------------

    [Fact]
    public void Get_LegacyIssueNumber_ReadsAttribute()
    {
        const string xml =
            "<dmodule><idstatus><dmaddres><dmc><avee>" +
            "<modelic>EX</modelic><sdc>A</sdc><chapnum>00</chapnum><section>0</section>" +
            "<subsect>0</subsect><subject>00</subject><discode>00</discode><discodev>A</discodev>" +
            "<incode>040</incode><incodev>A</incodev><itemloc>D</itemloc>" +
            "</avee></dmc><issno issno=\"007\" inwork=\"03\" type=\"changed\"/>" +
            "</dmaddres></idstatus></dmodule>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.Equal("007", Metadata.Get(doc, "issueNumber"));
        Assert.Equal("03", Metadata.Get(doc, "inWork"));
        Assert.Equal("007-03", Metadata.Get(doc, "issueInfo"));
        Assert.Equal("changed", Metadata.Get(doc, "issueType"));
        Assert.Equal("EX-A-00-00-00-00A-040A-D", Metadata.Get(doc, "dmCode"));
        Assert.Equal("EX", Metadata.Get(doc, "modelIdentCode"));
    }

    [Fact]
    public void Set_LegacyModelIdentCode_EditsElement()
    {
        const string xml = "<dmc><avee><modelic>EX</modelic></avee></dmc>";
        var doc = XmlUtils.ReadMem(xml);
        Assert.True(Metadata.Set(doc, "modelIdentCode", "AB"));
        Assert.Equal("AB", Metadata.Get(doc, "modelIdentCode"));
        Assert.Equal("AB", doc.SelectSingleNode("//modelic")!.InnerText);
    }

    // ---------------------------------------------------------------------
    // ICN file metadata
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("type", "icn")]
    [InlineData("code", "ICN-S1000DBIKE-AAA-D000000-0-U8025-00555-A-001-01")]
    [InlineData("issueNumber", "001")]
    [InlineData("securityClassification", "01")]
    public void GetIcn_DerivesFromFileName(string key, string expected)
    {
        const string name = "ICN-S1000DBIKE-AAA-D000000-0-U8025-00555-A-001-01.PNG";
        Assert.Equal(expected, Metadata.GetIcn(name, key));
    }

    [Fact]
    public void GetIcn_StripsDirectory()
    {
        const string path = "/some/dir/ICN-EX-00001-001-01.PNG";
        Assert.Equal("ICN-EX-00001-001-01", Metadata.GetIcn(path, "code"));
        Assert.Equal("01", Metadata.GetIcn(path, "securityClassification"));
        Assert.Equal("001", Metadata.GetIcn(path, "issueNumber"));
    }

    [Fact]
    public void GetIcn_UnknownKey_ReturnsNull()
    {
        Assert.Null(Metadata.GetIcn("ICN-EX-00001-001-01.PNG", "techName"));
    }

    [Fact]
    public void IcnKeys_ListsFourDerivableKeys()
    {
        Assert.Equal(new[] { "code", "issueNumber", "securityClassification", "type" }, Metadata.IcnKeys);
    }

    // ---------------------------------------------------------------------
    // Custom date format (-d)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("%Y-%m-%d", "2026-06-25")]
    [InlineData("%Y/%m/%d", "2026/06/25")]
    [InlineData("%d.%m.%Y", "25.06.2026")]
    [InlineData("%Y", "2026")]
    [InlineData("%y", "26")]
    [InlineData("%j", "176")]
    public void Get_IssueDate_HonoursDateFormat(string fmt, string expected)
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal(expected, Metadata.Get(doc, "issueDate", fmt));
    }

    [Fact]
    public void Get_IssueDate_NullFormat_UsesDefault()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal("2026-06-25", Metadata.Get(doc, "issueDate", null));
    }

    [Fact]
    public void Strftime_LiteralPercentAndUnknown()
    {
        var t = new DateTime(2026, 6, 25);
        Assert.Equal("100%", Metadata.Strftime("100%%", t));
        // Unknown specifier is emitted verbatim.
        Assert.Equal("%Q-2026", Metadata.Strftime("%Q-%Y", t));
    }

    // ---------------------------------------------------------------------
    // HasNode
    // ---------------------------------------------------------------------

    [Fact]
    public void HasNode_TrueForPresent_FalseForAbsent()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.HasNode(doc, "techName"));
        Assert.True(Metadata.HasNode(doc, "issueNumber"));
        Assert.False(Metadata.HasNode(doc, "pmTitle"));
        Assert.False(Metadata.HasNode(doc, "notAKey"));
    }

    // ---------------------------------------------------------------------
    // Condition content (get_cond_content): getter keys use the composite
    // getter; non-getter keys use raw node content.
    // ---------------------------------------------------------------------

    [Fact]
    public void GetConditionContent_GetterKey_UsesComposite()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        // dmCode has a composite getter.
        Assert.Equal("EX-A-00-00-00-00A-040A-D",
            Metadata.GetConditionContent(doc, "dmCode", null));
        // issueDate getter honours the date format.
        Assert.Equal("2026/06/25",
            Metadata.GetConditionContent(doc, "issueDate", "%Y/%m/%d"));
    }

    [Fact]
    public void GetConditionContent_NonGetterKey_UsesNodeContent()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        // issueNumber has no get pointer in C => condition uses node content,
        // which is the located node (the issueInfo element) text content.
        // The issueInfo element has no text, so content is empty (not the attr).
        Assert.Equal(string.Empty, Metadata.GetConditionContent(doc, "issueNumber", null));
        // techName: show==node content and no get => node content "Example".
        Assert.Equal("Example", Metadata.GetConditionContent(doc, "techName", null));
    }

    [Fact]
    public void GetConditionContent_MissingNode_ReturnsNull()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Null(Metadata.GetConditionContent(doc, "pmTitle", null));
    }
}
