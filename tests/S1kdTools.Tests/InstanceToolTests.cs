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
}
