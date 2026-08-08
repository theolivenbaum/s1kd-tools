using System.Xml;

namespace S1kdTools.Presentation.Tests;

/// <summary>
/// The API around the stylesheets: type detection, stylesheet access and the
/// options that reach the transform.
/// </summary>
public class PresentationApiTests
{
    [Fact]
    public void EveryObjectTypeHasAnEmbeddedStylesheet()
    {
        foreach (CsdbObjectTypeInfo info in CsdbObjectTypes.Catalogue)
        {
            string xsl = S1000DPresentation.GetStylesheet(info.Type);
            Assert.Contains("xsl:stylesheet", xsl, StringComparison.Ordinal);
            // Directly, or through another type's stylesheet (the cross-reference
            // tables build on the applicability one).
            Assert.Contains("xsl:import", xsl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCatalogueMapsEachSchemaAndContentElementToOneType()
    {
        var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = new HashSet<string>(StringComparer.Ordinal);

        foreach (CsdbObjectTypeInfo info in CsdbObjectTypes.Catalogue)
        {
            Assert.True(schemas.Add(info.Schema), $"Duplicate schema: {info.Schema}");
            Assert.True(elements.Add(info.ContentElement), $"Duplicate content element: {info.ContentElement}");
        }

        // Every enum value is in the catalogue.
        foreach (CsdbObjectType type in Enum.GetValues<CsdbObjectType>())
        {
            Assert.Equal(type, CsdbObjectTypes.Info(type).Type);
        }
    }

    [Fact]
    public void TypeIsDetectedFromTheContentElementWhenNoSchemaIsDeclared()
    {
        var dm = new XmlDocument();
        dm.LoadXml("<dmodule><identAndStatusSection/><content><procedure/></content></dmodule>");

        Assert.Equal(CsdbObjectType.Procedure, S1000DPresentation.DetectObjectType(dm));
    }

    [Fact]
    public void TheSchemaLocationWinsOverTheContentElement()
    {
        // A container data module holds a <refs>, not a schema-named content
        // element, so only the schema location identifies it.
        var dm = new XmlDocument();
        dm.LoadXml("""
            <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                     xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_6/xml_schema_flat/container.xsd">
              <identAndStatusSection/><content><container><refs/></container></content>
            </dmodule>
            """);

        Assert.Equal(CsdbObjectType.Container, S1000DPresentation.DetectObjectType(dm));
    }

    [Fact]
    public void AnUnknownObjectIsReportedRatherThanRendered()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<notAnS1000DObject/>");

        Assert.False(S1000DPresentation.TryDetectObjectType(doc, out _));
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => S1000DPresentation.DetectObjectType(doc));
        Assert.Contains("notAnS1000DObject", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsReachTheRenderedPage()
    {
        var options = new PresentationOptions
        {
            Publisher = "SYNTHETIC AEROSPACE",
            PublicationTitle = "COMPONENT MAINTENANCE MANUAL",
            Page = PageSize.A5,
            Watermark = "DRAFT",
            GraphicsDirectories = SampleObjects.Options.GraphicsDirectories,
        };

        XmlDocument fo = S1000DPresentation.TransformToFo(
            SampleObjects.Load(CsdbObjectType.Description), options);

        var ns = new XmlNamespaceManager(fo.NameTable);
        ns.AddNamespace("fo", "http://www.w3.org/1999/XSL/Format");

        XmlElement master = (XmlElement)fo.SelectSingleNode(
            "/fo:root/fo:layout-master-set/fo:simple-page-master", ns)!;
        Assert.Equal("148mm", master.GetAttribute("page-width"));
        Assert.Equal("210mm", master.GetAttribute("page-height"));

        string header = fo.SelectSingleNode(
            "/fo:root/fo:page-sequence/fo:static-content[@flow-name='xsl-region-before']", ns)!.InnerText;
        Assert.Contains("SYNTHETIC AEROSPACE", header, StringComparison.Ordinal);
        Assert.Contains("COMPONENT MAINTENANCE MANUAL", header, StringComparison.Ordinal);
        Assert.Contains("DRAFT", header, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTitleBlockCanBeTurnedOff()
    {
        XmlDocument dm = SampleObjects.Load(CsdbObjectType.Description);

        string with = S1000DPresentation.TransformToFo(dm, SampleObjects.Options).OuterXml;
        string without = S1000DPresentation.TransformToFo(
            dm, SampleObjects.Options with { IncludeTitleBlock = false }).OuterXml;

        Assert.Contains("Quality assurance", with, StringComparison.Ordinal);
        Assert.DoesNotContain("Quality assurance", without, StringComparison.Ordinal);
        Assert.True(without.Length < with.Length);
    }

    [Fact]
    public void AReferencedIcnIsResolvedToTheFileOnDisk()
    {
        XmlDocument prepared = S1000DPresentation.PrepareForPresentation(
            SampleObjects.Load(CsdbObjectType.Description), SampleObjects.Options);

        XmlElement graphic = prepared.GetElementsByTagName("graphic")
            .OfType<XmlElement>().First();
        string resolved = graphic.GetAttribute(S1000DPresentation.ResolvedGraphicAttribute);

        Assert.True(File.Exists(resolved), $"Unresolved ICN reference: '{resolved}'");
        // The renderer is given a path, not a URI: a file:// URI draws an empty frame.
        Assert.DoesNotContain("file://", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparingForPresentationDoesNotTouchTheCallersDocument()
    {
        XmlDocument original = SampleObjects.Load(CsdbObjectType.Description);
        string before = original.OuterXml;

        S1000DPresentation.PrepareForPresentation(original, SampleObjects.Options);

        Assert.Equal(before, original.OuterXml);
    }

    [Fact]
    public void AnUnresolvedIcnRendersAPlaceholderInsteadOfFailing()
    {
        // No graphics directories: the ICN cannot be found.
        XmlDocument fo = S1000DPresentation.TransformToFo(
            SampleObjects.Load(CsdbObjectType.Description), PresentationOptions.Default);

        var ns = new XmlNamespaceManager(fo.NameTable);
        ns.AddNamespace("fo", "http://www.w3.org/1999/XSL/Format");

        Assert.Null(fo.SelectSingleNode("//fo:external-graphic", ns));
        Assert.Contains("ICN-A350X-A-278100-A-U8025-00001-A-001-01", fo.OuterXml, StringComparison.Ordinal);
    }
}
