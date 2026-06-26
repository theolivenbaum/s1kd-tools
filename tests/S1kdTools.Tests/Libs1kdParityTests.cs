using System.IO;
using System.Xml;
using S1kdTools;

namespace S1kdTools.Tests;

/// <summary>
/// Parity port of the libs1kd C test suite
/// (<c>reference/tools/libs1kd/tests/tests.c</c>). The C program exercised the
/// public <c>libs1kd</c> API — BREX checking (<c>s1kdDocCheckDefaultBREX</c> /
/// <c>s1kdDocCheckBREX</c>), metadata get/set (<c>s1kdDocGetMetadata</c> /
/// <c>s1kdDocSetMetadata</c>), and applicability filtering
/// (<c>s1kdDocFilter</c>) — by printing results. These tests reproduce the same
/// flows against the C# library (<see cref="BrexCheck"/>, <see cref="Metadata"/>,
/// <see cref="Instance"/>) and assert on the observable outcomes, reusing the
/// fixtures shipped alongside the C tests (<c>test.xml</c> and <c>brex.xml</c>).
/// </summary>
public class Libs1kdParityTests
{
    // The fixtures shipped with the C test suite. They are resolved relative to
    // the repository's reference/ tree (the test runner's working directory is
    // the test project's output folder).
    private static readonly string FixtureDir = FindFixtureDir();

    private static string FindFixtureDir()
    {
        // Walk up from the test output directory until we find the reference
        // fixtures, so the test is independent of the build's working directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(
                dir.FullName, "reference", "tools", "libs1kd", "tests");
            if (File.Exists(Path.Combine(candidate, "test.xml")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate reference/tools/libs1kd/tests fixtures.");
    }

    private static XmlDocument LoadFixture(string name) =>
        XmlUtils.ReadDoc(Path.Combine(FixtureDir, name));

    // ---- test_brexcheck -----------------------------------------------------

    /// <summary>
    /// Mirrors <c>test_brexcheck</c>: checking <c>test.xml</c> against its
    /// applicable default BREX with VALUES|SNS|STRICT_SNS|NOTATIONS. The
    /// document declares a JPG NOTATION and uses &lt;emphasis&gt;, so against the
    /// (custom) <c>brex.xml</c> it must fail; against the standard default BREX
    /// it conforms structurally for those rules.
    /// </summary>
    [Fact]
    public void DefaultBrexCheck_ProducesReport()
    {
        XmlDocument doc = LoadFixture("test.xml");

        BrexCheckOptions opts = BrexCheckOptions.Values | BrexCheckOptions.Sns |
            BrexCheckOptions.StrictSns | BrexCheckOptions.Notations;

        int err = BrexCheck.CheckDefault(doc, opts, out XmlDocument report);

        // The C test simply prints PASS/FAIL and the report; we assert the
        // report is well-formed and the error count is non-negative.
        Assert.NotNull(report);
        Assert.NotNull(report.DocumentElement);
        Assert.Equal("brexCheck", report.DocumentElement!.Name);
        Assert.True(err >= 0);
    }

    /// <summary>
    /// Mirrors <c>test_brexcheck</c>'s custom-BREX branch: checking
    /// <c>test.xml</c> against <c>brex.xml</c>, which forbids both
    /// <c>//emphasis</c> (present in the document) and the JPG NOTATION
    /// (declared in the document's internal subset). This must report errors.
    /// </summary>
    [Fact]
    public void CustomBrexCheck_DetectsForbiddenEmphasisAndNotation()
    {
        XmlDocument doc = LoadFixture("test.xml");
        XmlDocument brex = LoadFixture("brex.xml");

        BrexCheckOptions opts = BrexCheckOptions.Values | BrexCheckOptions.Sns |
            BrexCheckOptions.StrictSns | BrexCheckOptions.Notations |
            BrexCheckOptions.VerboseLog;

        int err = BrexCheck.Check(doc, brex, opts, out XmlDocument report);

        Assert.True(err > 0, "Custom BREX should report violations for emphasis/notation.");

        // The structure-object rule on //emphasis must have been violated.
        XmlNode? emphasisError = report.SelectSingleNode(
            "//brex/error[objectPath = '//emphasis']");
        Assert.NotNull(emphasisError);

        // The JPG notation rule must have been violated.
        XmlNode? notationError = report.SelectSingleNode(
            "//notations/error[invalidNotation = 'JPG']");
        Assert.NotNull(notationError);
    }

    /// <summary>
    /// Mirrors <c>test_brexcheck_2</c>: a trivial document checked against the
    /// default / a trivial BREX conforms (no structure rules to violate).
    /// </summary>
    [Fact]
    public void TrivialDocAgainstTrivialBrex_Conforms()
    {
        XmlDocument doc = XmlUtils.ReadMem("<root/>");
        XmlDocument brex = XmlUtils.ReadMem("<root/>");

        int err = BrexCheck.Check(doc, brex, BrexCheckOptions.Values, out XmlDocument report);

        Assert.Equal(0, err);
        Assert.NotNull(report.DocumentElement);
    }

    // ---- test_metadata ------------------------------------------------------

    /// <summary>
    /// Mirrors <c>test_metadata</c>: read the S1000D issue and issueDate of
    /// <c>test.xml</c>, set new values, and read them back. The fixture is dated
    /// 2019-09-21.
    ///
    /// <para>
    /// Parity note: the fixture's schema URL is the flat <c>S1000D_6</c> form
    /// (no dash). Both the C <c>get_issue</c> and the C# <see cref="Metadata"/>
    /// use the regex <c>S1000D_([0-9]+)-([0-9]+)</c>, which requires a dash, so
    /// reading the <c>issue</c> of this fixture yields no value in both — the C
    /// program would print "(null)". Setting the issue (which rewrites the
    /// schema URL via the issue map) then reading it back round-trips as
    /// <c>4.1</c>, exactly as the C does.
    /// </para>
    /// </summary>
    [Fact]
    public void Metadata_GetSetIssueAndDate()
    {
        XmlDocument doc = LoadFixture("test.xml");

        // Flat S1000D_6 URL has no major-minor dash: get_issue yields null in C too.
        Assert.Null(Metadata.Get(doc, "issue"));

        Assert.True(Metadata.Set(doc, "issue", "4.1"));
        Assert.Equal("4.1", Metadata.Get(doc, "issue"));

        Assert.Equal("2019-09-21", Metadata.Get(doc, "issueDate"));

        Assert.True(Metadata.Set(doc, "issueDate", "1970-01-01"));
        Assert.Equal("1970-01-01", Metadata.Get(doc, "issueDate"));
    }

    /// <summary>
    /// Mirrors <c>test_metadata_2</c>: get/set <c>issueDate</c> on an in-memory
    /// fragment built from year/month/day attributes.
    /// </summary>
    [Fact]
    public void Metadata_IssueDateFromInMemoryFragment()
    {
        XmlDocument doc = XmlUtils.ReadMem(
            "<issueDate year=\"2020\" month=\"05\" day=\"01\"/>");

        Assert.Equal("2020-05-01", Metadata.Get(doc, "issueDate"));

        Assert.True(Metadata.Set(doc, "issueDate", "1970-01-01"));
        Assert.Equal("1970-01-01", Metadata.Get(doc, "issueDate"));
    }

    // ---- test_instance ------------------------------------------------------

    /// <summary>
    /// Mirrors <c>test_instance</c>: filter <c>test.xml</c> for version=A under
    /// each of the four filter modes. The fixture has two applic statements
    /// (app-A for version A, app-B for version B) referenced by two paras; with
    /// version=A assigned, the version-B content must be removed in every mode,
    /// and the version-A content retained.
    /// </summary>
    [Theory]
    [InlineData(FilterMode.Default)]
    [InlineData(FilterMode.Reduce)]
    [InlineData(FilterMode.Simplify)]
    [InlineData(FilterMode.Prune)]
    public void Instance_FilterByProductAttribute(FilterMode mode)
    {
        XmlDocument doc = LoadFixture("test.xml");

        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        XmlDocument outDoc = Instance.Filter(doc, app, mode);

        // The version-B para must be filtered out; the version-A para retained.
        Assert.Null(outDoc.SelectSingleNode("//para[@applicRefId='app-B']"));
        Assert.NotNull(outDoc.SelectSingleNode("//description"));

        // The non-applicable content is gone, so its text "B" must not survive.
        XmlNodeList? paras = outDoc.SelectNodes("//description/para");
        Assert.NotNull(paras);
        foreach (XmlNode p in paras!)
        {
            Assert.NotEqual("B", p.InnerText);
        }

        // The input document is not mutated (s1kdDocFilter clones).
        Assert.NotNull(doc.SelectSingleNode("//para[@applicRefId='app-B']"));
    }

    /// <summary>
    /// Mirrors <c>test_instance_2</c>: filtering a trivial document with an
    /// applicability assignment is a no-op that returns a valid document.
    /// </summary>
    [Fact]
    public void Instance_FilterTrivialDocument()
    {
        XmlDocument doc = XmlUtils.ReadMem("<root/>");

        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        XmlDocument outDoc = Instance.Filter(doc, app, FilterMode.Default);

        Assert.NotNull(outDoc.DocumentElement);
        Assert.Equal("root", outDoc.DocumentElement!.Name);
    }
}
