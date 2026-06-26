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

    // A multi-line object placing each ref on a known source line. The C reports
    // xmlGetLineNo(ref) — the line of the ref element's start tag. The -L list
    // output is "<path>:<line>:<ident>".
    private const string MultiLineObj =
        """
        <dmodule>
          <content>
            <description>
              <para>
                <functionalItemRef functionalItemNumber="FI001"/>
              </para>
              <warningRef warningIdentNumber="W001"/>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void ListRefs_ReportsCorrectSourceLines()
    {
        string dir = NewTempDir();
        try
        {
            string obj = WriteTemp(dir, "OBJ.XML", MultiLineObj);

            var (code, outText, _) = Run("-L", obj);
            Assert.Equal(0, code);
            // functionalItemRef start tag is on line 5; warningRef on line 7.
            Assert.Contains($"{obj}:5:Functional item FI001", outText);
            Assert.Contains($"{obj}:7:Warning W001", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnresolvedReference_ErrorReportsSourceLine()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            // functionalItemRef on line 5 references a non-existent item.
            string obj = WriteTemp(dir, "OBJ.XML",
                """
                <dmodule>
                  <content>
                    <description>
                      <para>
                        <functionalItemRef functionalItemNumber="FI999"/>
                      </para>
                    </description>
                  </content>
                </dmodule>
                """);

            var (code, _, err) = Run("-R", cir, obj);
            Assert.Equal(1, code);
            Assert.Contains($"{obj} (5): Functional item FI999 not found.", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void XmlReport_RefHasCorrectLineAttribute()
    {
        string dir = NewTempDir();
        try
        {
            string cir = WriteTemp(dir, "CIR.XML", Cir);
            string obj = WriteTemp(dir, "OBJ.XML", MultiLineObj);

            var (code, outText, _) = Run("-x", "-R", cir, obj);
            Assert.Equal(0, code);

            var report = new XmlDocument();
            report.LoadXml(outText.Trim());

            XmlElement? fin = report.SelectSingleNode("/repCheck/object/ref[@type='fin']") as XmlElement;
            Assert.NotNull(fin);
            Assert.Equal("5", fin!.GetAttribute("line"));

            XmlElement? warn = report.SelectSingleNode("/repCheck/object/ref[@type='warn']") as XmlElement;
            Assert.NotNull(warn);
            Assert.Equal("7", warn!.GetAttribute("line"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // A start tag spanning multiple lines: the reported line is where the start
    // tag terminates, mirroring libxml2's xmlGetLineNo.
    [Fact]
    public void ListRefs_MultiLineStartTag_ReportsTagEndLine()
    {
        string dir = NewTempDir();
        try
        {
            string obj = WriteTemp(dir, "OBJ.XML",
                """
                <dmodule>
                  <content>
                    <functionalItemRef
                       functionalItemNumber="FI001"
                       />
                  </content>
                </dmodule>
                """);

            var (code, outText, _) = Run("-L", obj);
            Assert.Equal(0, code);
            // Start tag opens on line 3 and terminates on line 5.
            Assert.Contains($"{obj}:5:Functional item FI001", outText);
        }
        finally { Directory.Delete(dir, true); }
    }
}
