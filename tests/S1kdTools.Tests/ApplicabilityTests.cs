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

    // ----------------------------------------------------------------------
    // AddCctDepends (mirrors add_cct_depends)
    // ----------------------------------------------------------------------

    private const string SimpleCct =
        """
        <cct>
          <cond id="cond001">
            <dependency dependencyTest="dep001" forCondValues="running"/>
          </cond>
          <applic id="dep001">
            <assert applicPropertyIdent="cond002" applicPropertyType="condition" applicPropertyValues="on"/>
          </applic>
        </cct>
        """;

    [Fact]
    public void AddCctDepends_AddsDependencyTestToMatchingAssert()
    {
        var doc = XmlUtils.ReadMem(
            """
            <applic id="app-1">
              <assert applicPropertyIdent="cond001" applicPropertyType="condition" applicPropertyValues="running"/>
            </applic>
            """);
        var cct = XmlUtils.ReadMem(SimpleCct);

        Applicability.AddCctDepends(doc, cct, null);

        // The original assert is replaced by an AND evaluate combining the
        // dependency test (cond002=on) with the original condition value.
        XmlNode? eval = doc.SelectSingleNode("//applic/evaluate[@andOr='and']");
        Assert.NotNull(eval);

        Assert.NotNull(eval!.SelectSingleNode(
            "assert[@applicPropertyIdent='cond002' and @applicPropertyType='condition' and @applicPropertyValues='on']"));
        Assert.NotNull(eval.SelectSingleNode(
            "assert[@applicPropertyIdent='cond001' and @applicPropertyType='condition' and @applicPropertyValues='running']"));

        // No bare assert for cond001 remains.
        Assert.Null(doc.SelectSingleNode("//applic/assert[@applicPropertyIdent='cond001']"));
    }

    [Fact]
    public void AddCctDepends_LeavesUnrelatedAssertsUntouched()
    {
        var doc = XmlUtils.ReadMem(
            """
            <applic id="app-1">
              <assert applicPropertyIdent="cond009" applicPropertyType="condition" applicPropertyValues="x"/>
            </applic>
            """);
        var cct = XmlUtils.ReadMem(SimpleCct);

        Applicability.AddCctDepends(doc, cct, null);

        // The assert does not use a dependant value, so it is unchanged.
        Assert.NotNull(doc.SelectSingleNode(
            "//applic/assert[@applicPropertyIdent='cond009' and @applicPropertyValues='x']"));
        Assert.Null(doc.SelectSingleNode("//evaluate"));
    }

    [Fact]
    public void AddCctDepends_OnlyAddsDependencyToDependantValueInSet()
    {
        // The assert uses a set (running|stopped); only "running" has a
        // dependency, so the result is an OR over the dependency-tested
        // "running" and the bare "stopped".
        var doc = XmlUtils.ReadMem(
            """
            <applic id="app-1">
              <assert applicPropertyIdent="cond001" applicPropertyType="condition" applicPropertyValues="running|stopped"/>
            </applic>
            """);
        var cct = XmlUtils.ReadMem(SimpleCct);

        Applicability.AddCctDepends(doc, cct, null);

        XmlNode? or = doc.SelectSingleNode("//applic/evaluate[@andOr='or']");
        Assert.NotNull(or);

        // The AND branch ties cond001=running to the dependency cond002=on.
        XmlNode? and = or!.SelectSingleNode("evaluate[@andOr='and']");
        Assert.NotNull(and);
        Assert.NotNull(and!.SelectSingleNode("assert[@applicPropertyIdent='cond002' and @applicPropertyValues='on']"));
        Assert.NotNull(and.SelectSingleNode("assert[@applicPropertyIdent='cond001' and @applicPropertyValues='running']"));

        // The non-dependant value remains a bare assert in the OR.
        Assert.NotNull(or.SelectSingleNode(
            "assert[@applicPropertyIdent='cond001' and @applicPropertyValues='stopped']"));
    }

    [Fact]
    public void AddCctDepends_ResolvesSubDependencies()
    {
        // cond001=running depends on cond002=on (dep001), which itself depends
        // on cond003=ready (dep002). Both should be injected.
        var cct = XmlUtils.ReadMem(
            """
            <cct>
              <cond id="cond001">
                <dependency dependencyTest="dep001" forCondValues="running"/>
              </cond>
              <cond id="cond002">
                <dependency dependencyTest="dep002" forCondValues="on"/>
              </cond>
              <applic id="dep001">
                <assert applicPropertyIdent="cond002" applicPropertyType="condition" applicPropertyValues="on"/>
              </applic>
              <applic id="dep002">
                <assert applicPropertyIdent="cond003" applicPropertyType="condition" applicPropertyValues="ready"/>
              </applic>
            </cct>
            """);
        var doc = XmlUtils.ReadMem(
            """
            <applic id="app-1">
              <assert applicPropertyIdent="cond001" applicPropertyType="condition" applicPropertyValues="running"/>
            </applic>
            """);

        Applicability.AddCctDepends(doc, cct, null);

        // cond003=ready (the sub-dependency) must appear somewhere in the result.
        Assert.NotNull(doc.SelectSingleNode(
            "//assert[@applicPropertyIdent='cond003' and @applicPropertyValues='ready']"));
        Assert.NotNull(doc.SelectSingleNode(
            "//assert[@applicPropertyIdent='cond001' and @applicPropertyValues='running']"));
    }

    [Fact]
    public void AddCctDepends_NoDependenciesIsNoOp()
    {
        var doc = XmlUtils.ReadMem(
            """
            <applic id="app-1">
              <assert applicPropertyIdent="cond001" applicPropertyType="condition" applicPropertyValues="running"/>
            </applic>
            """);
        var cct = XmlUtils.ReadMem("<cct><cond id=\"cond001\"/></cct>");

        Applicability.AddCctDepends(doc, cct, null);

        Assert.NotNull(doc.SelectSingleNode(
            "//applic/assert[@applicPropertyIdent='cond001' and @applicPropertyValues='running']"));
        Assert.Null(doc.SelectSingleNode("//evaluate"));
    }
}
