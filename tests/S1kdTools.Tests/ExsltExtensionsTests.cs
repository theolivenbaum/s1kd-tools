using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using S1kdTools.Xslt;

namespace S1kdTools.Tests;

/// <summary>
/// Exercises the EXSLT shim by compiling small inline stylesheets that call the
/// shimmed functions and asserting on the produced output. CWD-safe: every
/// stylesheet/input is parsed from an in-memory string, nothing touches disk.
/// </summary>
public class ExsltExtensionsTests
{
    // A trivial input document; the stylesheets below mostly ignore it.
    private const string Input = "<doc/>";

    private static XmlDocument RunStylesheet(string stylesheet)
    {
        var input = new XmlDocument();
        input.LoadXml(Input);

        using var styleReader = XmlReader.Create(new StringReader(stylesheet));
        XslCompiledTransform xslt = Exslt.Load(styleReader);
        return Exslt.Transform(xslt, input);
    }

    private static string Wrap(string namespaces, string body) =>
        "<xsl:stylesheet version=\"1.0\" " +
        "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
        namespaces + ">" +
        "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
        "<xsl:template match=\"/\"><result>" + body + "</result></xsl:template>" +
        "</xsl:stylesheet>";

    [Fact]
    public void StrReplace_ReplacesAllOccurrences()
    {
        string xsl = Wrap(
            "xmlns:str=\"http://exslt.org/strings\"",
            "<xsl:value-of select=\"str:replace('a-b-c', '-', '/')\"/>");

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("a/b/c", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void StrTokenize_SplitsOnDelimitersAndDropsEmpties()
    {
        // Two delimiters (space and comma); consecutive delimiters produce no
        // empty tokens, matching EXSLT.
        string xsl = Wrap(
            "xmlns:str=\"http://exslt.org/strings\"",
            "<xsl:for-each select=\"str:tokenize('one, two,,three', ' ,')\">" +
            "<t><xsl:value-of select=\".\"/></t></xsl:for-each>");

        XmlDocument result = RunStylesheet(xsl);

        var tokens = result.DocumentElement!.SelectNodes("t")!;
        Assert.Equal(3, tokens.Count);
        Assert.Equal("one", tokens[0]!.InnerText);
        Assert.Equal("two", tokens[1]!.InnerText);
        Assert.Equal("three", tokens[2]!.InnerText);
    }

    [Fact]
    public void StrSplit_SplitsOnLiteralSeparator()
    {
        string xsl = Wrap(
            "xmlns:str=\"http://exslt.org/strings\"",
            "<xsl:value-of select=\"count(str:split('a|b|c', '|'))\"/>");

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("3", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void StrPadding_ProducesFixedWidthString()
    {
        string xsl = Wrap(
            "xmlns:str=\"http://exslt.org/strings\"",
            "<xsl:value-of select=\"concat('[', str:padding(4, '*'), ']')\"/>");

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("[****]", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void MathMax_ReturnsLargestNumber()
    {
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:math=\"http://exslt.org/math\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:variable name=\"nums\">" +
            "<n>3</n><n>17</n><n>9</n>" +
            "</xsl:variable>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:value-of select=\"math:max(exsl:node-set($nums)/n)\" " +
            "xmlns:exsl=\"http://exslt.org/common\"/>" +
            "</result></xsl:template></xsl:stylesheet>";

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("17", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void MathMin_ReturnsSmallestNumber()
    {
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:math=\"http://exslt.org/math\" " +
            "xmlns:exsl=\"http://exslt.org/common\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:variable name=\"nums\"><n>3</n><n>17</n><n>9</n></xsl:variable>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:value-of select=\"math:min(exsl:node-set($nums)/n)\"/>" +
            "</result></xsl:template></xsl:stylesheet>";

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("3", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void SetDistinct_RemovesDuplicates()
    {
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:set=\"http://exslt.org/sets\" " +
            "xmlns:exsl=\"http://exslt.org/common\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:variable name=\"vals\">" +
            "<v>a</v><v>b</v><v>a</v><v>c</v><v>b</v>" +
            "</xsl:variable>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:for-each select=\"set:distinct(exsl:node-set($vals)/v)\">" +
            "<d><xsl:value-of select=\".\"/></d></xsl:for-each>" +
            "</result></xsl:template></xsl:stylesheet>";

        XmlDocument result = RunStylesheet(xsl);

        var distinct = result.DocumentElement!.SelectNodes("d")!;
        Assert.Equal(3, distinct.Count);
        Assert.Equal("a", distinct[0]!.InnerText);
        Assert.Equal("b", distinct[1]!.InnerText);
        Assert.Equal("c", distinct[2]!.InnerText);
    }

    [Fact]
    public void SetDifference_KeepsOnlyNodesNotInSecondSet()
    {
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:set=\"http://exslt.org/sets\" " +
            "xmlns:exsl=\"http://exslt.org/common\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:variable name=\"a\"><v>x</v><v>y</v><v>z</v></xsl:variable>" +
            "<xsl:variable name=\"b\"><v>y</v></xsl:variable>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:for-each select=\"set:difference(exsl:node-set($a)/v, exsl:node-set($b)/v)\">" +
            "<d><xsl:value-of select=\".\"/></d></xsl:for-each>" +
            "</result></xsl:template></xsl:stylesheet>";

        XmlDocument result = RunStylesheet(xsl);

        var diff = result.DocumentElement!.SelectNodes("d")!;
        Assert.Equal(2, diff.Count);
        Assert.Equal("x", diff[0]!.InnerText);
        Assert.Equal("z", diff[1]!.InnerText);
    }

    [Fact]
    public void ExslNodeSet_NativeFunctionConvertsRtf()
    {
        // exsl:node-set is native to XslCompiledTransform; this verifies our
        // Load/Transform pipeline does not interfere with it.
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:exsl=\"http://exslt.org/common\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:variable name=\"frag\"><a>1</a><a>2</a></xsl:variable>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:value-of select=\"count(exsl:node-set($frag)/a)\"/>" +
            "</result></xsl:template></xsl:stylesheet>";

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("2", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void DateYear_ParsesSuppliedDate()
    {
        string xsl = Wrap(
            "xmlns:date=\"http://exslt.org/dates-and-times\"",
            "<xsl:value-of select=\"date:year('2017-08-16')\"/>");

        XmlDocument result = RunStylesheet(xsl);

        Assert.Equal("2017", result.DocumentElement!.InnerText);
    }

    [Fact]
    public void CreateArgumentList_RegistersAllModules()
    {
        // Smoke test the helper: it should produce an argument list usable by a
        // stylesheet that references several modules at once.
        XsltArgumentList args = Exslt.CreateArgumentList();
        Assert.NotNull(args.GetExtensionObject(Exslt.StringsNamespace));
        Assert.NotNull(args.GetExtensionObject(Exslt.MathNamespace));
        Assert.NotNull(args.GetExtensionObject(Exslt.DatesNamespace));
        Assert.NotNull(args.GetExtensionObject(Exslt.SetsNamespace));
        Assert.NotNull(args.GetExtensionObject(Exslt.CommonNamespace));
    }

    [Fact]
    public void Transform_PassesExplicitStylesheetParameter()
    {
        string xsl =
            "<xsl:stylesheet version=\"1.0\" " +
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
            "xmlns:str=\"http://exslt.org/strings\">" +
            "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>" +
            "<xsl:param name=\"sep\"/>" +
            "<xsl:template match=\"/\"><result>" +
            "<xsl:value-of select=\"count(str:split('p-q-r', $sep))\"/>" +
            "</result></xsl:template></xsl:stylesheet>";

        var input = new XmlDocument();
        input.LoadXml(Input);
        using var styleReader = XmlReader.Create(new StringReader(xsl));
        XslCompiledTransform xslt = Exslt.Load(styleReader);

        XmlDocument result = Exslt.Transform(xslt, input, ("sep", "-"));

        Assert.Equal("3", result.DocumentElement!.InnerText);
    }
}
