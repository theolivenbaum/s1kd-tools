using System.Text;
using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Presentation.Tests;

/// <summary>
/// Renders the sample CSDB object of every type to PDF. Each rendered sample is
/// written to <see cref="SampleObjects.OutputDirectory"/> so the output can be
/// looked at, not only asserted on.
/// </summary>
public class RenderSamplesTests
{
    [Theory]
    [MemberData(nameof(SampleObjects.AllTypes), MemberType = typeof(SampleObjects))]
    public void EverySampleRendersToPdf(CsdbObjectType type)
    {
        CsdbObjectTypeInfo info = CsdbObjectTypes.Info(type);
        Assert.True(File.Exists(SampleObjects.PathFor(type)),
            $"Missing sample object for {type}: {SampleObjects.PathFor(type)}");

        XmlDocument sample = SampleObjects.Load(type);

        // The sample must be recognised from its own content, not from its file name.
        Assert.Equal(type, S1000DPresentation.DetectObjectType(sample));

        using Stream pdf = S1000DPresentation.RenderToPdf(sample, SampleObjects.Options);
        byte[] bytes = ToArray(pdf);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.True(bytes.Length > 2000, $"{type} produced a suspiciously small PDF ({bytes.Length} bytes).");

        File.WriteAllBytes(Path.Combine(SampleObjects.OutputDirectory, info.Schema + ".pdf"), bytes);
    }

    [Theory]
    [MemberData(nameof(SampleObjects.AllTypes), MemberType = typeof(SampleObjects))]
    public void EverySampleProducesWellFormedFo(CsdbObjectType type)
    {
        XmlDocument fo = S1000DPresentation.TransformToFo(SampleObjects.Load(type), SampleObjects.Options);

        Assert.NotNull(fo.DocumentElement);
        Assert.Equal("root", fo.DocumentElement!.LocalName);
        Assert.Equal("http://www.w3.org/1999/XSL/Format", fo.DocumentElement.NamespaceURI);

        var ns = new XmlNamespaceManager(fo.NameTable);
        ns.AddNamespace("fo", "http://www.w3.org/1999/XSL/Format");

        Assert.NotNull(fo.SelectSingleNode("/fo:root/fo:layout-master-set/fo:simple-page-master", ns));
        Assert.NotNull(fo.SelectSingleNode("/fo:root/fo:page-sequence/fo:flow", ns));

        // The running header and footer are what make the output look like a
        // page-oriented manual rather than a dump of the XML.
        Assert.Equal(2, fo.SelectNodes("/fo:root/fo:page-sequence/fo:static-content", ns)!.Count);

        // Every flow must carry text; an empty page-sequence means the content
        // model of that schema was not covered by its stylesheet.
        string flowText = fo.SelectSingleNode("/fo:root/fo:page-sequence/fo:flow", ns)!.InnerText;
        Assert.True(flowText.Trim().Length > 200,
            $"{type} rendered almost no text ({flowText.Trim().Length} characters).");
    }

    [Fact]
    public void SamplesExistForEveryType()
    {
        var missing = new List<string>();
        foreach (CsdbObjectTypeInfo info in CsdbObjectTypes.Catalogue)
        {
            if (!File.Exists(SampleObjects.PathFor(info.Type)))
            {
                missing.Add(info.Schema + ".xml");
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void SeveralObjectsMergeIntoOnePdf()
    {
        XmlDocument[] objects =
        [
            SampleObjects.Load(CsdbObjectType.Description),
            SampleObjects.Load(CsdbObjectType.Procedure),
            SampleObjects.Load(CsdbObjectType.IllustratedPartsCatalog),
        ];

        using Stream merged = S1000DPresentation.RenderToPdf(objects, SampleObjects.Options);
        byte[] bytes = ToArray(merged);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        using Stream single = S1000DPresentation.RenderToPdf(objects[0], SampleObjects.Options);
        Assert.True(bytes.Length > ToArray(single).Length,
            "The merged document should be larger than any one of its parts.");

        File.WriteAllBytes(Path.Combine(SampleObjects.OutputDirectory, "merged-publication.pdf"), bytes);
    }

    [Theory]
    [InlineData(RenderTool.RenderFormat.Text)]
    [InlineData(RenderTool.RenderFormat.Markdown)]
    [InlineData(RenderTool.RenderFormat.Html)]
    public void TheSameStylesheetsAlsoDriveTheTextFormats(RenderTool.RenderFormat format)
    {
        using Stream output = S1000DPresentation.Render(
            SampleObjects.Load(CsdbObjectType.Procedure), format, SampleObjects.Options);

        string text = new StreamReader(output, Encoding.UTF8).ReadToEnd();
        Assert.Contains("Slat", text, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ToArray(Stream stream)
    {
        if (stream is MemoryStream ms)
        {
            return ms.ToArray();
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
