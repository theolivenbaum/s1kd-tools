using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class InstanceToolTests
{
    /// <summary>
    /// A data module with a referencedApplicGroup and content tagged by
    /// applicRefId for two mutually exclusive versions (A and B), plus an
    /// always-applicable paragraph.
    /// </summary>
    private const string ApplicDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
                <issueInfo issueNumber="002" inWork="01"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2026" month="06" day="25"/>
                <dmTitle>
                  <techName>Example</techName>
                  <infoName>Description</infoName>
                </dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="changed">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <referencedApplicGroup>
              <applic id="app-A">
                <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
              </applic>
              <applic id="app-B">
                <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="B"/>
              </applic>
            </referencedApplicGroup>
            <description>
              <para>Always present.</para>
              <para applicRefId="app-A">Only for version A.</para>
              <para applicRefId="app-B">Only for version B.</para>
            </description>
          </content>
        </dmodule>
        """;

    private static (int code, string outText, string errText) Run(string fixturePath, params string[] args)
    {
        var tool = new InstanceTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { fixturePath };
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-inst-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, xml);
        return path;
    }

    private static string WriteFixture(string dir, string name, string xml)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, xml);
        return path;
    }

    // ---- CIR resolution fixtures ----

    /// <summary>An instance DM that references a warning, a part and a functional item via CIRs.</summary>
    private const string CirRefDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="100"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="US"/>
                <issueInfo issueNumber="000" inWork="01"/>
              </dmIdent>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <warningsAndCautionsRef>
              <warningRef id="warn-001" warningIdentNumber="warn-00001"/>
            </warningsAndCautionsRef>
            <procedure>
              <reqSpares>
                <spareDescr>
                  <partRef manufacturerCodeValue="12345" partNumberValue="001"/>
                  <reqQuantity>1</reqQuantity>
                </spareDescr>
              </reqSpares>
              <para>
                <functionalItemRef functionalItemNumber="func-00001"/>
              </para>
            </procedure>
          </content>
        </dmodule>
        """;

    private const string WarningCir =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="0A4"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="US"/>
                <issueInfo issueNumber="000" inWork="01"/>
              </dmIdent>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <commonRepository>
              <warningRepository>
                <warningSpec>
                  <warningIdent warningIdentNumber="warn-00001"/>
                  <warningAndCautionPara>This is an example warning.</warningAndCautionPara>
                </warningSpec>
              </warningRepository>
            </commonRepository>
          </content>
        </dmodule>
        """;

    private const string PartCir =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="00G"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="US"/>
                <issueInfo issueNumber="000" inWork="01"/>
              </dmIdent>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <commonRepository>
              <partRepository>
                <partSpec>
                  <partIdent manufacturerCodeValue="12345" partNumberValue="001"/>
                  <itemIdentData>
                    <descrForPart>Some sort of gizmo</descrForPart>
                    <shortName>Gizmo</shortName>
                  </itemIdentData>
                </partSpec>
              </partRepository>
            </commonRepository>
          </content>
        </dmodule>
        """;

    // ---- core filtering via the CLI ----

    [Fact]
    public void Filter_RemovesNonApplicableContent_KeepsApplicable()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-s", "version:prodattr=A");
            Assert.Equal(0, code);
            Assert.Contains("Always present.", outText);
            Assert.Contains("Only for version A.", outText);
            Assert.DoesNotContain("Only for version B.", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Filter_OtherValue_RemovesOppositeContent()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-s", "version:prodattr=B");
            Assert.Equal(0, code);
            Assert.Contains("Only for version B.", outText);
            Assert.DoesNotContain("Only for version A.", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Filter_Default_KeepsAnnotationsByDefault()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-s", "version:prodattr=A");
            Assert.Equal(0, code);
            // Default mode keeps the referencedApplicGroup (only content stripped).
            Assert.Contains("referencedApplicGroup", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reduce_RemovesUnambiguousAnnotations()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            // -a removes annotations that are unambiguously true/false; both
            // applics resolve to true (A) or false (B) so the group empties out.
            var (code, outText, _) = Run(path, "-s", "version:prodattr=A", "-a");
            Assert.Equal(0, code);
            Assert.DoesNotContain("referencedApplicGroup", outText);
            // The applicRefId on the kept content should be cleaned up.
            Assert.DoesNotContain("applicRefId", outText);
            Assert.Contains("Only for version A.", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Tag_MarksNonApplicableInsteadOfRemoving()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-s", "version:prodattr=A", "-T");
            Assert.Equal(0, code);
            // Non-applicable B content is retained but marked.
            Assert.Contains("Only for version B.", outText);
            Assert.Contains("notApplicable", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoMatchingDefs_LeavesDocumentUnfiltered()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);
            // With no -s, all content stays.
            Assert.Contains("Only for version A.", outText);
            Assert.Contains("Only for version B.", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MalformedAssign_ReturnsBadApplic()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, _, _) = Run(path, "-s", "notavaliddef");
            Assert.Equal(4, code);
        }
        finally { File.Delete(path); }
    }

    // ---- metadata setters ----

    [Fact]
    public void SetCode_RewritesDmCode()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-c", "NEW-A-00-00-00-00A-040A-A");
            Assert.Equal(0, code);
            Assert.Contains("modelIdentCode=\"NEW\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetIssue_UpdatesIssueInfo()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-n", "004-02");
            Assert.Equal(0, code);
            Assert.Contains("issueNumber=\"004\"", outText);
            Assert.Contains("inWork=\"02\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetLanguage_UpdatesLanguage()
    {
        string path = WriteFixture(ApplicDm);
        try
        {
            var (code, outText, _) = Run(path, "-l", "fr-FR");
            Assert.Equal(0, code);
            Assert.Contains("languageIsoCode=\"fr\"", outText);
            Assert.Contains("countryIsoCode=\"FR\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OutFile_WritesFilteredInstance()
    {
        string path = WriteFixture(ApplicDm);
        string outPath = Path.Combine(Path.GetTempPath(), $"s1kd-inst-out-{Guid.NewGuid():N}.XML");
        try
        {
            var (code, _, _) = Run(path, "-s", "version:prodattr=A", "-o", outPath);
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath));
            string written = File.ReadAllText(outPath);
            Assert.Contains("Only for version A.", written);
            Assert.DoesNotContain("Only for version B.", written);
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- library API ----

    [Fact]
    public void LibraryFilter_DoesNotMutateInput()
    {
        var doc = XmlUtils.ReadMem(ApplicDm);
        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        var result = Instance.Filter(doc, app, FilterMode.Default);

        // Input untouched.
        Assert.NotNull(doc.SelectSingleNode("//para[@applicRefId='app-B']"));
        // Result filtered.
        Assert.Null(result.SelectSingleNode("//para[@applicRefId='app-B']"));
        Assert.NotNull(result.SelectSingleNode("//para[@applicRefId='app-A']"));
    }

    [Fact]
    public void LibraryFilter_ReduceRemovesGroup()
    {
        var doc = XmlUtils.ReadMem(ApplicDm);
        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        var result = Instance.Filter(doc, app, FilterMode.Reduce);

        Assert.Null(result.SelectSingleNode("//referencedApplicGroup"));
        var kept = result.SelectSingleNode("//para[text()='Only for version A.']");
        Assert.NotNull(kept);
        Assert.False(((XmlElement)kept!).HasAttribute("applicRefId"));
    }

    [Fact]
    public void LibraryFilter_EmptyDefs_ReturnsCopy()
    {
        var doc = XmlUtils.ReadMem(ApplicDm);
        var app = new Applicability();

        var result = Instance.Filter(doc, app, FilterMode.Default);

        Assert.NotNull(result.SelectSingleNode("//para[@applicRefId='app-A']"));
        Assert.NotNull(result.SelectSingleNode("//para[@applicRefId='app-B']"));
    }

    // ---- CIR resolution ----

    [Fact]
    public void Cir_ResolvesWarningRef_InlinesWarningContent()
    {
        string dm = WriteFixture(CirRefDm);
        string cir = WriteFixture(WarningCir);
        try
        {
            var (code, outText, _) = Run(dm, "-R", cir);
            Assert.Equal(0, code);
            // The warningRef should be resolved into a <warning> with the para text.
            Assert.Contains("This is an example warning.", outText);
            Assert.Contains("<warning", outText);
            // The reference wrapper element is replaced by the resolved element name.
            Assert.Contains("<warningsAndCautions", outText);
            Assert.DoesNotContain("warningRef", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(cir);
        }
    }

    [Fact]
    public void Cir_ResolvesPartRef_InlinesName()
    {
        string dm = WriteFixture(CirRefDm);
        string cir = WriteFixture(PartCir);
        try
        {
            var (code, outText, _) = Run(dm, "-R", cir);
            Assert.Equal(0, code);
            // The spareDescr/partRef gets the part's name + shortName inlined.
            Assert.Contains("Some sort of gizmo", outText);
            Assert.Contains("<name>", outText);
            Assert.Contains("Gizmo", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(cir);
        }
    }

    [Fact]
    public void Cir_AddsRepositorySourceDmIdent()
    {
        string dm = WriteFixture(CirRefDm);
        string cir = WriteFixture(WarningCir);
        try
        {
            var (code, outText, _) = Run(dm, "-R", cir);
            Assert.Equal(0, code);
            Assert.Contains("repositorySourceDmIdent", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(cir);
        }
    }

    [Fact]
    public void Cir_NoRepositoryIdent_OptionSuppressesSource()
    {
        string dm = WriteFixture(CirRefDm);
        string cir = WriteFixture(WarningCir);
        try
        {
            var (code, outText, _) = Run(dm, "-R", cir, "-S");
            Assert.Equal(0, code);
            Assert.DoesNotContain("repositorySourceDmIdent", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(cir);
        }
    }

    [Fact]
    public void Cir_AutoFind_ResolvesViaSearchDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-cir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        // Auto-find requires DMC- prefixed filenames recognised by Csdb.IsDataModule.
        string dm = WriteFixture(dir, "DMC-TEST-A-00-00-00-00A-100A-A_000-01_EN-US.XML", CirRefDm);
        WriteFixture(dir, "DMC-TEST-A-00-00-00-00A-0A4A-A_000-01_EN-US.XML", WarningCir);
        try
        {
            var (code, outText, _) = Run(dm, "-R", "*", "-d", dir);
            Assert.Equal(0, code);
            Assert.Contains("This is an example warning.", outText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dump_OutputsBuiltInXslt()
    {
        string dm = WriteFixture(CirRefDm);
        try
        {
            var (code, outText, _) = Run(dm, "-D", "warningRepository");
            Assert.Equal(0, code);
            Assert.Contains("xsl:stylesheet", outText);
            Assert.Contains("warningRef", outText);
        }
        finally { File.Delete(dm); }
    }

    [Fact]
    public void Dump_UnknownType_ReturnsError()
    {
        string dm = WriteFixture(CirRefDm);
        try
        {
            var (code, _, _) = Run(dm, "-D", "notARepository");
            Assert.Equal(7, code);
        }
        finally { File.Delete(dm); }
    }

    // ---- product (PCT) filtering ----

    /// <summary>A PCT with two products, each assigning a different version value.</summary>
    private const string Pct =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="258"
                        infoCodeVariant="A" itemLocationCode="A"/>
                <language languageIsoCode="en" countryIsoCode="US"/>
                <issueInfo issueNumber="000" inWork="01"/>
              </dmIdent>
            </dmAddress>
            <dmStatus issueType="new">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <productCrossRefTable>
              <product id="prod-A">
                <assign applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValue="A"/>
              </product>
              <product id="prod-B">
                <assign applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValue="B"/>
              </product>
            </productCrossRefTable>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Pct_ProductById_FiltersToThatProductsApplicability()
    {
        string dm = WriteFixture(ApplicDm);
        string pct = WriteFixture(Pct);
        try
        {
            // Product prod-A assigns version=A, so version-B content drops out.
            var (code, outText, _) = Run(dm, "-P", pct, "-p", "prod-A");
            Assert.Equal(0, code);
            Assert.Contains("Always present.", outText);
            Assert.Contains("Only for version A.", outText);
            Assert.DoesNotContain("Only for version B.", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(pct);
        }
    }

    [Fact]
    public void Pct_ProductByPrimaryKey_FiltersCorrectly()
    {
        string dm = WriteFixture(ApplicDm);
        string pct = WriteFixture(Pct);
        try
        {
            // Identify the product by its assign primary key instead of @id.
            var (code, outText, _) = Run(dm, "-P", pct, "-p", "version:prodattr=B");
            Assert.Equal(0, code);
            Assert.Contains("Only for version B.", outText);
            Assert.DoesNotContain("Only for version A.", outText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(pct);
        }
    }

    [Fact]
    public void Pct_UnknownProduct_WarnsAndKeepsAllContent()
    {
        string dm = WriteFixture(ApplicDm);
        string pct = WriteFixture(Pct);
        try
        {
            var (code, outText, errText) = Run(dm, "-P", pct, "-p", "prod-Z");
            Assert.Equal(0, code);
            // No definitions assigned, so nothing is filtered.
            Assert.Contains("Only for version A.", outText);
            Assert.Contains("Only for version B.", outText);
            Assert.Contains("No product matching", errText);
        }
        finally
        {
            File.Delete(dm);
            File.Delete(pct);
        }
    }

    [Fact]
    public void LibraryLoadApplicFromPct_AssignsValues()
    {
        var defsDoc = XmlUtils.NewDocument();
        var defs = defsDoc.CreateElement("applic");
        defsDoc.AppendChild(defs);

        var pct = XmlUtils.ReadMem(Pct);
        int n = Instance.LoadApplicFromPct(defs, pct, "prod-A");

        Assert.Equal(1, n);
        var assert = defs.SelectSingleNode("assert[@applicPropertyIdent='version']") as XmlElement;
        Assert.NotNull(assert);
        Assert.Equal("A", assert!.GetAttribute("applicPropertyValues"));
    }
}
