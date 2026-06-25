using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class AppCheckToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new AppCheckTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteTemp(string contents, string suffix = ".XML")
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-appcheck-{Guid.NewGuid():N}{suffix}");
        File.WriteAllText(path, contents);
        return path;
    }

    /* ---- Standalone check: broken cross-reference after filtering ---- */

    // DM applicable to version A or B; a levelledPara applies only to B and is
    // referenced by another para. Filtering for A breaks the reference -> invalid.
    private const string BrokenRefDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmStatus>
              <applic>
                <evaluate andOr="or">
                  <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
                  <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="B"/>
                </evaluate>
              </applic>
            </dmStatus>
          </identAndStatusSection>
          <referencedApplicGroup>
            <applic id="app-VersionB">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="B"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <levelledPara id="par-0001" applicRefId="app-VersionB">
                <title>Features of version B</title>
                <para>...</para>
              </levelledPara>
              <levelledPara>
                <title>More information</title>
                <para>Refer to <internalRef internalRefId="par-0001"/>.</para>
              </levelledPara>
            </description>
          </content>
        </dmodule>
        """;

    // Same content but the reference target is not conditional -> always valid.
    private const string ValidDm =
        """
        <dmodule>
          <referencedApplicGroup>
            <applic id="app-VersionB">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="B"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <levelledPara id="par-0001">
                <title>Common feature</title>
                <para>...</para>
              </levelledPara>
              <levelledPara applicRefId="app-VersionB">
                <title>More information</title>
                <para>Refer to <internalRef internalRefId="par-0001"/>.</para>
              </levelledPara>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Standalone_BrokenReference_IsInvalid()
    {
        string path = WriteTemp(BrokenRefDm);
        try
        {
            var (code, _, err) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains("is invalid when", err);
            Assert.Contains("prodattr version = A", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Standalone_NoBrokenReference_IsValid()
    {
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, _, _) = Run(path);
            Assert.Equal(0, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verbose_ReportsSuccessForValidObject()
    {
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, _, err) = Run("-v", path);
            Assert.Equal(0, code);
            Assert.Contains("passed the applicability check", err);
        }
        finally { File.Delete(path); }
    }

    /* ---- XML report ---- */

    [Fact]
    public void XmlReport_ContainsAppCheckAndObject()
    {
        string path = WriteTemp(ValidDm);
        try
        {
            var (_, outText, _) = Run("-x", path);
            Assert.Contains("<appCheck", outText);
            Assert.Contains("type=\"standalone\"", outText);
            Assert.Contains("<object", outText);
            Assert.Contains("valid=\"yes\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlReport_MarksInvalidObject()
    {
        string path = WriteTemp(BrokenRefDm);
        try
        {
            var (_, outText, _) = Run("-x", path);
            Assert.Contains("valid=\"no\"", outText);
            Assert.Contains("<asserts", outText);
        }
        finally { File.Delete(path); }
    }

    /* ---- Property-definition check (-s) ---- */

    private const string DmUsingUndefinedAttr =
        """
        <dmodule>
          <referencedApplicGroup>
            <applic id="app-1">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="Z"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <para applicRefId="app-1">Conditional text.</para>
            </description>
          </content>
        </dmodule>
        """;

    private const string ActFixture =
        """
        <dmodule>
          <content>
            <applicCrossRefTable>
              <productAttribute id="version">
                <name>Version</name>
                <enumeration applicPropertyValues="A|B|C"/>
              </productAttribute>
            </applicCrossRefTable>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Strict_UndefinedValue_IsReported()
    {
        string act = WriteTemp(ActFixture);
        string dm = WriteTemp(DmUsingUndefinedAttr);
        try
        {
            // Custom check + strict isolates the property-definition check.
            var (code, _, err) = Run("-c", "-s", "-A", act, dm);
            Assert.Equal(1, code);
            Assert.Contains("is not a defined value of prodattr version", err);
        }
        finally { File.Delete(act); File.Delete(dm); }
    }

    [Fact]
    public void Strict_DefinedValue_Passes()
    {
        string act = WriteTemp(ActFixture);
        string dm = WriteTemp(BrokenRefDm); // uses version B which is defined
        try
        {
            var (code, _, err) = Run("-c", "-s", "-A", act, dm);
            Assert.Equal(0, code);
            Assert.DoesNotContain("is not a defined value", err);
        }
        finally { File.Delete(act); File.Delete(dm); }
    }

    [Fact]
    public void Strict_UndefinedProperty_NoActReported()
    {
        // No ACT provided and none referenced -> the property cannot be resolved.
        string dm = WriteTemp(DmUsingUndefinedAttr);
        try
        {
            var (code, _, err) = Run("-c", "-s", dm);
            Assert.Equal(1, code);
            Assert.Contains("prodattr version is not defined", err);
        }
        finally { File.Delete(dm); }
    }

    [Fact]
    public void Strict_IgnoredProperty_IsSkipped()
    {
        string dm = WriteTemp(DmUsingUndefinedAttr);
        try
        {
            var (code, _, _) = Run("-c", "-s", "-i", "version:prodattr", dm);
            Assert.Equal(0, code);
        }
        finally { File.Delete(dm); }
    }

    /* ---- Nested applicability check (-n) ---- */

    // Whole object applies to A,B; a step applies to C (not a subset) -> error.
    private const string NestedDm =
        """
        <dmodule>
          <content>
            <description>
              <applic>
                <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
                <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="B"/>
              </applic>
              <referencedApplicGroup>
                <applic id="app-C">
                  <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="C"/>
                </applic>
              </referencedApplicGroup>
              <proceduralStep applicRefId="app-C">
                <para>Step for C only.</para>
              </proceduralStep>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Nested_StepNotSubsetOfWhole_IsReported()
    {
        string path = WriteTemp(NestedDm);
        try
        {
            var (code, _, err) = Run("-c", "-n", path);
            Assert.Equal(1, code);
            Assert.Contains("not a subset of the applicability", err);
        }
        finally { File.Delete(path); }
    }

    /* ---- Redundant applicability check (-R) ---- */

    private const string RedundantDm =
        """
        <dmodule>
          <content>
            <description>
              <referencedApplicGroup>
                <applic id="app-A">
                  <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
                </applic>
              </referencedApplicGroup>
              <proceduralStep applicRefId="app-A">
                <para>Step A</para>
                <figure applicRefId="app-A">
                  <title>Fig</title>
                </figure>
              </proceduralStep>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Redundant_SameAnnotationOnChild_IsReported()
    {
        string path = WriteTemp(RedundantDm);
        try
        {
            var (code, _, err) = Run("-c", "-R", path);
            Assert.Equal(1, code);
            Assert.Contains("has the same applicability as its parent", err);
        }
        finally { File.Delete(path); }
    }

    /* ---- Duplicate applicability check (-D) ---- */

    private const string DuplicateDm =
        """
        <dmodule>
          <referencedApplicGroup>
            <applic id="app-0001">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
            </applic>
            <applic id="app-0002">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description><para>Text.</para></description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Duplicate_IdenticalAnnotations_IsReported()
    {
        string path = WriteTemp(DuplicateDm);
        try
        {
            var (code, _, err) = Run("-c", "-D", path);
            Assert.Equal(1, code);
            Assert.Contains("is a duplicate of annotation", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Duplicate_DistinctAnnotations_NotReported()
    {
        string path = WriteTemp(NestedDm); // distinct annotations
        try
        {
            var (code, _, _) = Run("-c", "-D", path);
            Assert.Equal(0, code);
        }
        finally { File.Delete(path); }
    }

    /* ---- Misc ---- */

    [Fact]
    public void Version_PrintsVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("6.9.2", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-appcheck", outText);
    }

    [Fact]
    public void BadObject_ReturnsExit2()
    {
        var (code, _, _) = Run(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.XML"));
        Assert.Equal(2, code);
    }

    [Fact]
    public void ValidFilenames_PrintsPathForValidObject()
    {
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, outText, _) = Run("-F", path);
            Assert.Equal(0, code);
            Assert.Contains(path, outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Filenames_PrintsPathForInvalidObject()
    {
        string path = WriteTemp(BrokenRefDm);
        try
        {
            var (code, outText, _) = Run("-f", path);
            Assert.Equal(1, code);
            Assert.Contains(path, outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Summary_PrintsStats()
    {
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, _, err) = Run("-T", path);
            Assert.Equal(0, code);
            Assert.Contains("passed", err);
        }
        finally { File.Delete(path); }
    }
}
