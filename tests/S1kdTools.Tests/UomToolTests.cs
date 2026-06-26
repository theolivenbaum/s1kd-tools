using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class UomToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new UomTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-uom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // A minimal data module fragment with several quantities.
    private const string Doc = """
        <?xml version="1.0" encoding="UTF-8"?>
        <dmodule>
          <content>
            <description>
              <para>Width:
                <quantity quantityType="qty01">
                  <quantityGroup>
                    <quantityValue quantityUnitOfMeasure="mm">275</quantityValue>
                  </quantityGroup>
                </quantity>
              </para>
              <para>Length:
                <quantity quantityType="qty01">
                  <quantityGroup>
                    <quantityValue quantityUnitOfMeasure="in">2</quantityValue>
                  </quantityGroup>
                </quantity>
              </para>
              <para>Temperature:
                <quantity quantityType="qty03">
                  <quantityGroup>
                    <quantityValue quantityUnitOfMeasure="degC">23</quantityValue>
                  </quantityGroup>
                </quantity>
              </para>
            </description>
          </content>
        </dmodule>
        """;

    private static XmlDocument Parse(string xml) => XmlUtils.ReadMem(xml);

    private static string ValueWithUom(XmlDocument doc, string uom)
    {
        var node = doc.SelectSingleNode($"//quantityValue[@quantityUnitOfMeasure='{uom}']");
        return node!.InnerText.Trim();
    }

    // ----------------------------------------------------------------------

    [Fact]
    public void SingleConversion_ConvertsValueAndUnit()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "in.XML");
            File.WriteAllText(path, Doc);

            // in -> cm : 2 * 2.54 = 5.08
            var (code, outText, _) = Run("-u", "in", "-t", "cm", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("5.08", ValueWithUom(doc, "cm"));
            // The original "in" unit must be gone (rewritten to cm).
            Assert.Null(doc.SelectSingleNode("//quantityValue[@quantityUnitOfMeasure='in']"));
            // Untouched units stay as-is.
            Assert.Equal("275", ValueWithUom(doc, "mm"));
            Assert.Equal("23", ValueWithUom(doc, "degC"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TemperatureConversion_UsesFormulaWithOffset()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "t.XML");
            File.WriteAllText(path, Doc);

            // degC -> degF : 23 * 9/5 + 32 = 73.4
            var (code, outText, _) = Run("-u", "degC", "-t", "degF", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("73.4", ValueWithUom(doc, "degF"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Format_AppliesNumberPicture()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "f.XML");
            File.WriteAllText(path, Doc);

            // mm -> in : 275 div 25.4 = 10.8267..., format 0.000 -> 10.827
            var (code, outText, _) = Run("-u", "mm", "-t", "in", "-F", "0.000", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("10.827", ValueWithUom(doc, "in"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomFormula_OverridesBuiltIn()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "e.XML");
            File.WriteAllText(path, Doc);

            // Override: in -> cm using a custom formula ($value * 100).
            var (code, outText, _) = Run("-u", "in", "-t", "cm", "-e", "$value * 100", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("200", ValueWithUom(doc, "cm"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preset_SI_ConvertsImperialUnits()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "si.XML");
            File.WriteAllText(path, Doc);

            // SI preset converts in -> cm (2 * 2.54 = 5.08) and degF->degC etc.
            var (code, outText, _) = Run("-s", "SI", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("5.08", ValueWithUom(doc, "cm"));
            // mm and degC are not part of the SI preset's source units; unchanged.
            Assert.Equal("275", ValueWithUom(doc, "mm"));
            Assert.Equal("23", ValueWithUom(doc, "degC"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CustomSet_FromFile_IsApplied()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "set.XML");
            File.WriteAllText(path, Doc);

            string setPath = Path.Combine(dir, "myset.xml");
            File.WriteAllText(setPath, """
                <?xml version="1.0"?>
                <uom>
                  <convert from="mm" to="in"/>
                </uom>
                """);

            var (code, outText, _) = Run("-S", setPath, path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // 275 div 25.4 = 10.8267 -> default 0.## -> 10.83
            Assert.Equal("10.83", ValueWithUom(doc, "in"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Overwrite_WritesBackToFile()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "ow.XML");
            File.WriteAllText(path, Doc);

            var (code, _, _) = Run("-f", "-u", "in", "-t", "cm", path);
            Assert.Equal(0, code);

            var doc = XmlUtils.ReadDoc(path);
            Assert.Equal("5.08", ValueWithUom(doc, "cm"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Duplicate_AppendsParenthesisedConversion()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "dup.XML");
            File.WriteAllText(path, Doc);

            var (code, outText, _) = Run("-d", "-u", "in", "-t", "cm", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // The original quantity is kept unchanged (still "in" with value 2).
            Assert.Equal("2", ValueWithUom(doc, "in"));
            // A parenthesised duplicate with the converted value was inserted.
            var dup = doc.SelectSingleNode("//s1kd-uom_DUPLICATE");
            Assert.NotNull(dup);
            Assert.Contains("(", dup!.InnerText);
            Assert.Contains(")", dup.InnerText);
            // The duplicate holds the converted quantity (5.08 cm).
            Assert.Equal("5.08", dup.SelectSingleNode(".//quantityValue[@quantityUnitOfMeasure='cm']")!.InnerText.Trim());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DashT_WithoutFrom_FailsWithExitNoUom()
    {
        var (code, _, err) = Run("-t", "cm");
        Assert.Equal(2, code);
        Assert.Contains("Unit conversions must be specified", err);
    }

    [Fact]
    public void DashE_WithoutFrom_FailsWithExitNoUom()
    {
        var (code, _, err) = Run("-e", "$value * 2");
        Assert.Equal(2, code);
        Assert.Contains("Unit conversions must be specified", err);
    }

    [Fact]
    public void DumpUom_PrintsBuiltInConfiguration()
    {
        var (code, outText, _) = Run("--dump-uom");
        Assert.Equal(0, code);
        Assert.Contains("<uom>", outText);
        Assert.Contains("<convert", outText);
        Assert.Contains("formula", outText);
    }

    [Fact]
    public void DumpUomDisplay_PrintsBuiltInDisplayConfiguration()
    {
        var (code, outText, _) = Run("--dump-uomdisplay");
        Assert.Equal(0, code);
        Assert.Contains("<uomDisplay>", outText);
    }

    [Fact]
    public void Version_PrintsVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("s1kd-uom", outText);
        Assert.Contains("1.20.0", outText);
    }

    [Fact]
    public void GroupLevelUom_IsResolvedFromQuantityGroup()
    {
        string dir = TempDir();
        try
        {
            const string groupDoc = """
                <?xml version="1.0" encoding="UTF-8"?>
                <dmodule><content><description>
                  <quantity>
                    <quantityGroup quantityUnitOfMeasure="in">
                      <quantityValue>2</quantityValue>
                    </quantityGroup>
                  </quantity>
                </description></content></dmodule>
                """;
            string path = Path.Combine(dir, "grp.XML");
            File.WriteAllText(path, groupDoc);

            var (code, outText, _) = Run("-u", "in", "-t", "cm", path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // Value converted (2 * 2.54 = 5.08) and group unit rewritten to cm.
            var grp = (XmlElement)doc.SelectSingleNode("//quantityGroup")!;
            Assert.Equal("cm", grp.GetAttribute("quantityUnitOfMeasure"));
            Assert.Equal("5.08", doc.SelectSingleNode("//quantityValue")!.InnerText.Trim());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FormulaEvaluator_HandlesDivAndPrecedence()
    {
        // 23 * (9 div 5) + 32 = 73.4
        Assert.Equal(73.4, FormulaEvaluator.Evaluate("$value * (9 div 5) + 32", 23), 6);
        // ($value - 32) * (5 div 9) at 212 = 100
        Assert.Equal(100.0, FormulaEvaluator.Evaluate("($value - 32) * (5 div 9)", 212), 6);
        // scientific notation
        Assert.Equal(2e6, FormulaEvaluator.Evaluate("$value * 1e+6", 2), 6);
    }

    // ---- -p / -P display preformatting -----------------------------------

    [Fact]
    public void Preformat_RendersQuantityAsValueAndUnit()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "pre.XML");
            File.WriteAllText(path, Doc);

            // Imperial: "." decimal, "," grouping. degC renders as " °C", mm as " mm".
            var (code, outText, _) = Run("-p", "imperial", path);
            Assert.Equal(0, code);

            // The quantity elements are replaced by rendered inline text.
            Assert.DoesNotContain("<quantity", outText);
            Assert.Contains("275 mm", outText);
            Assert.Contains("23 °C", outText); // 23 °C
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preformat_SI_UsesCommaDecimalAndSpaceGrouping()
    {
        string dir = TempDir();
        try
        {
            const string bigDoc = """
                <?xml version="1.0" encoding="UTF-8"?>
                <dmodule><content><description><para>
                  <quantity><quantityGroup>
                    <quantityValue quantityUnitOfMeasure="mm">1234.5</quantityValue>
                  </quantityGroup></quantity>
                </para></description></content></dmodule>
                """;
            string path = Path.Combine(dir, "si.XML");
            File.WriteAllText(path, bigDoc);

            var (code, outText, _) = Run("-p", "SI", path);
            Assert.Equal(0, code);
            // 1234.5 -> "1 234,5" (space grouping, comma decimal) + " mm".
            Assert.Contains("1 234,5 mm".Replace(" ", " "), outText.Replace(" ", " "));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preformat_RendersSuperScriptMarkupFromConfig()
    {
        string dir = TempDir();
        try
        {
            const string areaDoc = """
                <?xml version="1.0" encoding="UTF-8"?>
                <dmodule><content><description><para>
                  <quantity><quantityGroup>
                    <quantityValue quantityUnitOfMeasure="cm2">5</quantityValue>
                  </quantityGroup></quantity>
                </para></description></content></dmodule>
                """;
            string path = Path.Combine(dir, "area.XML");
            File.WriteAllText(path, areaDoc);

            var (code, outText, _) = Run("-p", "imperial", path);
            Assert.Equal(0, code);

            // cm2 display is " cm<superScript>2</superScript>".
            var doc = Parse(outText);
            var sup = doc.SelectSingleNode("//superScript");
            Assert.NotNull(sup);
            Assert.Equal("2", sup!.InnerText);
            Assert.Contains("5 cm", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preformat_AfterConversion_FormatsConvertedValue()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "conv.XML");
            File.WriteAllText(path, Doc);

            // Convert in -> cm (2 * 2.54 = 5.08), then preformat: "5.08 cm".
            var (code, outText, _) = Run("-u", "in", "-t", "cm", "-p", "imperial", path);
            Assert.Equal(0, code);
            Assert.DoesNotContain("<quantity", outText);
            Assert.Contains("5.08 cm", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preformat_CustomUomDisplayFile_IsApplied()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "cust.XML");
            File.WriteAllText(path, Doc);

            // Custom .uomdisplay overriding the "mm" display string.
            string dispPath = Path.Combine(dir, "mydisp.xml");
            File.WriteAllText(dispPath, """
                <?xml version="1.0" encoding="UTF-8"?>
                <uomDisplay>
                  <format name="custom" decimalSeparator="." groupingSeparator=","/>
                  <uoms>
                    <uom name="mm"> millimetres</uom>
                  </uoms>
                </uomDisplay>
                """);

            var (code, outText, _) = Run("-p", "custom", "-P", dispPath, path);
            Assert.Equal(0, code);
            Assert.Contains("275 millimetres", outText);
            // degC is not in the custom config -> default " degC".
            Assert.Contains("23 degC", outText);
        }
        finally { Directory.Delete(dir, true); }
    }
}
