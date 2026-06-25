using System.Xml;
using S1kdTools;

namespace S1kdTools.Tests;

public class ApplicabilityTests
{
    private static XmlNode Parse(string xml)
    {
        var doc = XmlUtils.ReadMem(xml);
        return doc.DocumentElement!;
    }

    [Fact]
    public void Assert_TrueWhenValueMatches()
    {
        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        var assertNode = Parse(
            "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>");

        Assert.True(app.Eval(assertNode, assume: false));
    }

    [Fact]
    public void Assert_FalseWhenValueDiffers()
    {
        var app = new Applicability();
        app.Assign("version", "prodattr", "A");

        var assertNode = Parse(
            "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"B\"/>");

        Assert.False(app.Eval(assertNode, assume: false));
    }

    [Fact]
    public void Assert_UndefinedProperty_DependsOnAssume()
    {
        var app = new Applicability(); // no definitions
        var assertNode = Parse(
            "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>");

        Assert.True(app.Eval(assertNode, assume: true));
        Assert.False(app.Eval(assertNode, assume: false));
    }

    [Fact]
    public void Evaluate_AndOr()
    {
        var app = new Applicability();
        app.Assign("version", "prodattr", "A");
        app.Assign("weather", "condition", "cold");

        var and = Parse(
            "<evaluate andOr=\"and\">" +
            "  <assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>" +
            "  <assert applicPropertyIdent=\"weather\" applicPropertyType=\"condition\" applicPropertyValues=\"cold\"/>" +
            "</evaluate>");
        Assert.True(Applicability.EvalEvaluate(app.Definitions, and, false));

        var or = Parse(
            "<evaluate andOr=\"or\">" +
            "  <assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"Z\"/>" +
            "  <assert applicPropertyIdent=\"weather\" applicPropertyType=\"condition\" applicPropertyValues=\"cold\"/>" +
            "</evaluate>");
        Assert.True(Applicability.EvalEvaluate(app.Definitions, or, false));
    }

    [Fact]
    public void RangeValue_IsApplic()
    {
        var app = new Applicability();
        app.Assign("temp", "condition", "25");

        var assertNode = Parse(
            "<assert applicPropertyIdent=\"temp\" applicPropertyType=\"condition\" applicPropertyValues=\"20~30\"/>");

        Assert.True(app.Eval(assertNode, assume: false));
    }
}
