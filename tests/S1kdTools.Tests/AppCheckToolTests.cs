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
    //
    // The fixtures carry an unreachable noNamespaceSchemaLocation so the
    // in-process schema validator (run by appcheck's default validator path in
    // standalone/PCT/all modes) degrades gracefully (no local schema -> warning,
    // not an error) and the applicability logic under test is isolated.
    private const string BrokenRefDm =
        """
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://example.invalid/descript.xsd">
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
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://example.invalid/descript.xsd">
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

    /* ---- CCT dependency injection (-~) ---- */

    // CCT: condition cond001 value "running" depends on cond002 = "on".
    private const string DepCct =
        """
        <dmodule>
          <content>
            <condCrossRefTable>
              <condType id="ct-state">
                <name>State</name>
                <enumeration applicPropertyValues="running|stopped"/>
              </condType>
              <condType id="ct-power">
                <name>Power</name>
                <enumeration applicPropertyValues="on|off"/>
              </condType>
              <cond id="cond001" condTypeRefId="ct-state">
                <name>Engine</name>
                <dependency dependencyTest="dep001" forCondValues="running"/>
              </cond>
              <cond id="cond002" condTypeRefId="ct-power">
                <name>Ignition</name>
              </cond>
              <applic id="dep001">
                <assert applicPropertyIdent="cond002" applicPropertyType="condition" applicPropertyValues="on"/>
              </applic>
            </condCrossRefTable>
          </content>
        </dmodule>
        """;

    // DM whose content is applicable when cond001 = running.
    private const string DmUsingCond =
        """
        <dmodule>
          <referencedApplicGroup>
            <applic id="app-run">
              <assert applicPropertyIdent="cond001" applicPropertyType="condition" applicPropertyValues="running"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <para applicRefId="app-run">Engine is running.</para>
            </description>
          </content>
        </dmodule>
        """;

    /* ---- Default schema validation of the filtered instance ---- */

    // A standalone object with no schema: the in-process schema validator (run
    // on each filtered combination) reports "has no schema" -> exit code 1, just
    // as the C tool's default s1kd-validate would.
    private const string SchemalessDm =
        """
        <dmodule>
          <referencedApplicGroup>
            <applic id="app-A">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <para applicRefId="app-A">Conditional text.</para>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Dependencies_InjectsDependentConditionIntoCheck()
    {
        string cct = WriteTemp(DepCct);
        string dm = WriteTemp(DmUsingCond);
        try
        {
            // Standalone check with CCT dependencies. The dependency on cond002
            // is injected into the object's applicability before the property
            // sets are extracted, so cond002 shows up in the checked combinations.
            var (code, outText, _) = Run("-C", cct, "-~", "-x", dm);
            Assert.Equal(0, code);
            Assert.Contains("applicPropertyIdent=\"cond002\"", outText);
            // The CCT was loaded and reported.
            Assert.Contains("<cct", outText);
        }
        finally { File.Delete(cct); File.Delete(dm); }
    }

    [Fact]
    public void Dependencies_NotRequested_DoesNotInjectDependentCondition()
    {
        string cct = WriteTemp(DepCct);
        string dm = WriteTemp(DmUsingCond);
        try
        {
            // Without -~, the dependency is not injected; only cond001 is checked.
            var (code, outText, _) = Run("-C", cct, "-x", dm);
            Assert.Equal(0, code);
            Assert.DoesNotContain("applicPropertyIdent=\"cond002\"", outText);
        }
        finally { File.Delete(cct); File.Delete(dm); }
    }

    [Fact]
    public void Dependencies_CustomMode_LoadsCctAndReports()
    {
        string cct = WriteTemp(DepCct);
        string dm = WriteTemp(DmUsingCond);
        try
        {
            // -~ in custom mode (no -s) still locates and loads the CCT to inject
            // dependencies, adding the cct object node to the report.
            var (code, outText, _) = Run("-c", "-C", cct, "-~", "-x", dm);
            Assert.Equal(0, code);
            Assert.Contains("<cct", outText);
        }
        finally { File.Delete(cct); File.Delete(dm); }
    }

    [Fact]
    public void Default_SchemalessFilteredInstance_FailsValidation()
    {
        string path = WriteTemp(SchemalessDm);
        try
        {
            // Standalone mode filters per value combination and runs the default
            // schema validator; a schemaless instance fails -> invalid.
            var (code, _, _) = Run(path);
            Assert.Equal(1, code);
        }
        finally { File.Delete(path); }
    }

    /* ---- Custom validators (-e) ---- */

    [Fact]
    public void Exec_CustomValidatorThatFails_MarksObjectInvalid()
    {
        // -e replaces the default validators. Running the ported schema validator
        // on this schemaless instance fails, so the object is reported invalid.
        string path = WriteTemp(SchemalessDm);
        try
        {
            var (code, _, _) = Run("-e", "s1kd-validate", path);
            Assert.Equal(1, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Exec_UnknownValidatorCommand_IsReportedAndFails()
    {
        // A custom validator that does not resolve to a ported in-process tool
        // cannot be reproduced; it is reported and counted as a failure.
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, _, err) = Run("-e", "no-such-validator --flag", path);
            Assert.Equal(1, code);
            Assert.Contains("is not an available in-process tool", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Exec_CustomValidatorThatPasses_KeepsObjectValid()
    {
        // The schema-bearing ValidDm degrades gracefully (no local schema ->
        // warning, not error), so the custom schema validator passes.
        string path = WriteTemp(ValidDm);
        try
        {
            var (code, _, _) = Run("-e", "s1kd-validate", path);
            Assert.Equal(0, code);
        }
        finally { File.Delete(path); }
    }

    /* ---- BREX check (-b) ---- */

    // A BREX DM that prohibits the //prohibited element. Filenames are matched
    // by DM code, so the file must be named to match the object's brexDmRef.
    private const string BrexProhibitDm =
        """
        <dmodule>
          <content>
            <brex>
              <contextRules>
                <structureObjectRule>
                  <brDecisionRef brDecisionIdentNumber="BREX-APPCHK-00001"/>
                  <objectPath allowedObjectFlag="0">//prohibited</objectPath>
                  <objectUse>The prohibited element must not be used.</objectUse>
                </structureObjectRule>
              </contextRules>
            </brex>
          </content>
        </dmodule>
        """;

    // The brexDmRef dmCode below must match the BREX file name written to disk.
    private const string DmRefsBrexWithProhibited =
        """
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://example.invalid/descript.xsd">
          <identAndStatusSection>
            <dmStatus>
              <brexDmRef>
                <dmRef>
                  <dmRefIdent>
                    <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="022" infoCodeVariant="A" itemLocationCode="D"/>
                  </dmRefIdent>
                </dmRef>
              </brexDmRef>
            </dmStatus>
          </identAndStatusSection>
          <referencedApplicGroup>
            <applic id="app-A">
              <assert applicPropertyIdent="version" applicPropertyType="prodattr" applicPropertyValues="A"/>
            </applic>
          </referencedApplicGroup>
          <content>
            <description>
              <prohibited applicRefId="app-A">Bad content for version A.</prohibited>
            </description>
          </content>
        </dmodule>
        """;

    // The filename derived from the brexDmRef dmCode above (issue/lang wildcards
    // are matched by StrMatch, so a concrete issue/lang in the name is fine).
    private const string BrexFileName =
        "DMC-TEST-A-00-00-00-00A-022A-D_001-00_EN-US.XML";

    [Fact]
    public void Brex_ViolationInFilteredInstance_IsInvalid()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-appcheck-brex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, BrexFileName), BrexProhibitDm);
            string obj = Path.Combine(dir, "object.XML");
            File.WriteAllText(obj, DmRefsBrexWithProhibited);

            // With -b, appcheck runs the BREX check on each filtered instance; the
            // version-A combination keeps the prohibited element -> BREX error.
            var (code, _, _) = Run("-b", "-d", dir, obj);
            Assert.Equal(1, code);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Brex_NoViolation_RemainsValid()
    {
        // Same object/BREX but without -b: only the default schema validator
        // runs, which degrades gracefully for the schema-bearing object -> valid.
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-appcheck-brex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, BrexFileName), BrexProhibitDm);
            string obj = Path.Combine(dir, "object.XML");
            File.WriteAllText(obj, DmRefsBrexWithProhibited);

            var (code, _, _) = Run("-d", dir, obj);
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
