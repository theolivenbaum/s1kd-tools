using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class RepCheckToolTests
{
    // A CIR data module defining a functional item, an enterprise, a part,
    // a warning and a zone.
    private const string Cir = """
        <dmodule>
          <content>
            <commonRepository>
              <functionalItemRepository>
                <functionalItemSpec>
                  <functionalItemIdent functionalItemNumber="FI001"/>
                </functionalItemSpec>
              </functionalItemRepository>
              <enterpriseRepository>
                <enterpriseSpec>
                  <enterpriseIdent manufacturerCodeValue="U8025"/>
                </enterpriseSpec>
              </enterpriseRepository>
              <partRepository>
                <partSpec>
                  <partIdent manufacturerCodeValue="U8025" partNumberValue="PN123"/>
                </partSpec>
              </partRepository>
              <warningRepository>
                <warningSpec>
                  <warningIdent warningIdentNumber="W001"/>
                </warningSpec>
              </warningRepository>
              <zoneRepository>
                <zoneSpec>
                  <zoneIdent zoneNumber="Z100"/>
                </zoneSpec>
              </zoneRepository>
            </commonRepository>
          </content>
        </dmodule>
        """;

    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new RepCheckTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteTemp(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-rep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolvedReference_ReturnsZero()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI001\"/></content></dmodule>");

            var (code, _, _) = Run("-R", cir, obj);
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnresolvedReference_ReturnsOne()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI999\"/></content></dmodule>");

            var (code, _, err) = Run("-R", cir, obj);
            Assert.Equal(1, code);
            Assert.Contains("not found", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PartReference_ResolvesOnBothAttributes()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string ok = WriteTemp(dir, "OK.XML",
                "<dmodule><content><partRef manufacturerCodeValue=\"U8025\" partNumberValue=\"PN123\"/></content></dmodule>");
            string bad = WriteTemp(dir, "BAD.XML",
                "<dmodule><content><partRef manufacturerCodeValue=\"U8025\" partNumberValue=\"PNXXX\"/></content></dmodule>");

            Assert.Equal(0, Run("-R", cir, ok).code);
            Assert.Equal(1, Run("-R", cir, bad).code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Enterprise_Warning_Zone_References_Resolve()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content>" +
                "<responsiblePartnerCompany enterpriseCode=\"U8025\"/>" +
                "<warningRef warningIdentNumber=\"W001\"/>" +
                "<zoneRef zoneNumber=\"Z100\"/>" +
                "</content></dmodule>");

            var (code, _, _) = Run("-R", cir, obj);
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TypeFilter_OnlyChecksMatchingType()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            // Bad functional item, but good warning. Filtering on "warn" should pass.
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content>" +
                "<functionalItemRef functionalItemNumber=\"FI999\"/>" +
                "<warningRef warningIdentNumber=\"W001\"/>" +
                "</content></dmodule>");

            Assert.Equal(0, Run("-t", "warn", "-R", cir, obj).code);
            Assert.Equal(1, Run("-t", "fin", "-R", cir, obj).code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListRefs_PrintsReferencesWithoutValidating()
    {
        string dir = NewTempDir();
        try
        {
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI001\"/></content></dmodule>");

            var (code, outText, _) = Run("-L", obj);
            Assert.Equal(0, code);
            Assert.Contains("Functional item FI001", outText);
            Assert.Contains(obj, outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void XmlReport_MarksObjectValidity()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI001\"/></content></dmodule>");

            var (code, outText, _) = Run("-x", "-R", cir, obj);
            Assert.Equal(0, code);

            var report = new XmlDocument();
            report.LoadXml(outText.Trim());
            XmlElement? o = report.SelectSingleNode("/repCheck/object") as XmlElement;
            Assert.NotNull(o);
            Assert.Equal("yes", o!.GetAttribute("valid"));

            XmlElement? r = report.SelectSingleNode("/repCheck/object/ref") as XmlElement;
            Assert.NotNull(r);
            Assert.Equal("fin", r!.GetAttribute("type"));
            Assert.Equal("Functional item FI001", r.GetAttribute("name"));
            Assert.Equal(cir, r.GetAttribute("cir"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void XmlReport_MarksInvalidObject()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI999\"/></content></dmodule>");

            var (code, outText, _) = Run("-x", "-q", "-R", cir, obj);
            Assert.Equal(1, code);

            var report = new XmlDocument();
            report.LoadXml(outText.Trim());
            XmlElement? o = report.SelectSingleNode("/repCheck/object") as XmlElement;
            Assert.NotNull(o);
            Assert.Equal("no", o!.GetAttribute("valid"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AutoFindCir_WithStar()
    {
        string dir = NewTempDir();
        try
        {
            WriteTemp(dir, "DMC-CIR-A-00-00-00-00A-000A-D_001-00_EN-US.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI001\"/></content></dmodule>");

            // Search the temp dir for CIRs automatically.
            var (code, _, _) = Run("-R", "*", "-d", dir, obj);
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AllRefs_ResolvesIndirectPartReference()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><spareDescr><identNumber>" +
                "<manufacturerCode>U8025</manufacturerCode>" +
                "<partAndSerialNumber><partNumber>PN123</partNumber></partAndSerialNumber>" +
                "</identNumber></spareDescr></content></dmodule>");

            // Without -A, the indirect reference is ignored (passes trivially).
            Assert.Equal(0, Run("-R", cir, obj).code);
            // With -A, the indirect part reference is validated and resolves.
            Assert.Equal(0, Run("-A", "-R", cir, obj).code);
        }
        finally { Directory.Delete(dir, true); }
    }

    // A custom extraction stylesheet that recognises a non-standard <myRef num="">
    // element (which the built-in rules ignore) and decorates it so the reference
    // resolves against a functionalItemIdent in the CIR.
    private const string CustomXsl = """
        <?xml version="1.0" encoding="UTF-8"?>
        <xsl:transform
          xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
          xmlns:s1kd-repcheck="urn:s1kd-tools:s1kd-repcheck"
          version="1.0">
          <xsl:template match="@*|node()">
            <xsl:copy>
              <xsl:apply-templates select="@*|node()"/>
            </xsl:copy>
          </xsl:template>
          <xsl:template match="myRef">
            <xsl:variable name="num" select="@num"/>
            <xsl:copy>
              <xsl:apply-templates select="@*"/>
              <xsl:attribute name="s1kd-repcheck:type">fin</xsl:attribute>
              <xsl:attribute name="s1kd-repcheck:name">
                <xsl:text>My ref </xsl:text>
                <xsl:value-of select="$num"/>
              </xsl:attribute>
              <xsl:attribute name="s1kd-repcheck:test">
                <xsl:text>//functionalItemIdent[@functionalItemNumber='</xsl:text>
                <xsl:value-of select="$num"/>
                <xsl:text>']</xsl:text>
              </xsl:attribute>
              <xsl:apply-templates select="node()"/>
            </xsl:copy>
          </xsl:template>
        </xsl:transform>
        """;

    [Fact]
    public void CustomXsl_ExtractsAndResolvesCustomReference()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string xsl = WriteTemp(dir, "custom.xsl", CustomXsl);
            string ok = WriteTemp(dir, "OK.XML",
                "<dmodule><content><myRef num=\"FI001\"/></content></dmodule>");
            string bad = WriteTemp(dir, "BAD.XML",
                "<dmodule><content><myRef num=\"FI999\"/></content></dmodule>");

            Assert.Equal(0, Run("-X", xsl, "-R", cir, ok).code);
            Assert.Equal(1, Run("-X", xsl, "-q", "-R", cir, bad).code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomXsl_IgnoresBuiltInReferenceTypes()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string xsl = WriteTemp(dir, "custom.xsl", CustomXsl);
            // A standard functionalItemRef: the built-in rules would check it,
            // but the custom XSLT does not decorate it, so it is not validated.
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI999\"/></content></dmodule>");

            // No decorated refs -> trivially passes under the custom stylesheet.
            Assert.Equal(0, Run("-X", xsl, "-R", cir, obj).code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomXsl_ListRefsUsesCustomNames()
    {
        string dir = NewTempDir();
        try
        {
            string xsl = WriteTemp(dir, "custom.xsl", CustomXsl);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><myRef num=\"FI001\"/></content></dmodule>");

            var (code, outText, _) = Run("-X", xsl, "-L", obj);
            Assert.Equal(0, code);
            Assert.Contains("My ref FI001", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomXsl_XmlReportOmitsToolAttributes()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string xsl = WriteTemp(dir, "custom.xsl", CustomXsl);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><myRef num=\"FI001\"/></content></dmodule>");

            var (code, outText, _) = Run("-X", xsl, "-x", "-R", cir, obj);
            Assert.Equal(0, code);

            var report = new XmlDocument();
            report.LoadXml(outText.Trim());
            XmlElement? r = report.SelectSingleNode("/repCheck/object/ref") as XmlElement;
            Assert.NotNull(r);
            Assert.Equal("fin", r!.GetAttribute("type"));
            Assert.Equal("My ref FI001", r.GetAttribute("name"));
            // The copied reference element must not retain the tool-added attrs.
            Assert.DoesNotContain("s1kd-repcheck:test", r.InnerXml);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomXsl_DumpXsl_OutputsCustomStylesheet()
    {
        string dir = NewTempDir();
        try
        {
            string xsl = WriteTemp(dir, "custom.xsl", CustomXsl);
            var (code, outText, _) = Run("-X", xsl, "-D");
            Assert.Equal(0, code);
            Assert.Contains("myRef", outText);
            Assert.DoesNotContain("functionalItemRef", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DumpXsl_OutputsStylesheet()
    {
        var (code, outText, _) = Run("-D");
        Assert.Equal(0, code);
        Assert.Contains("s1kd-repcheck:test", outText);
        Assert.Contains("functionalItemRef", outText);
    }

    [Fact]
    public void Version_PrintsVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("1.10.0", outText);
        Assert.Contains("s1kd-repcheck", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-repcheck", outText);
    }

    [Fact]
    public void ValidFilenames_PrintsValidObjectPath()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML",
                "<dmodule><content><functionalItemRef functionalItemNumber=\"FI001\"/></content></dmodule>");

            var (code, outText, _) = Run("-F", "-R", cir, obj);
            Assert.Equal(0, code);
            Assert.Contains(obj, outText);
        }
        finally { Directory.Delete(dir, true); }
    }
}
