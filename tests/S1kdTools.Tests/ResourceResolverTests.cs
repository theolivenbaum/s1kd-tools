using System.Text;
using S1kdTools.Editing;
using S1kdTools.Editor.Server;

namespace S1kdTools.Tests;

/// <summary>
/// A CSDB that is not a folder of files.
///
/// The tools resolve names from disk because that is where a CSDB usually is, but
/// a publishing organisation's is as likely to be a content management system, an
/// object store or a zip. The claim these tests make executable is that such a
/// CSDB supplies an <see cref="IResourceResolver"/> and nothing else changes: an
/// editing stylesheet, its imports, a presentation stylesheet, its imports and an
/// illustration all come out of memory here, and the same editor and the same
/// page preview come back.
/// </summary>
public class ResourceResolverTests
{
    private const string Module =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmAddressItems>
                <dmTitle><techName>Actuator</techName><infoName>Installation</infoName></dmTitle>
              </dmAddressItems>
            </dmAddress>
          </identAndStatusSection>
          <content>
            <description>
              <para>Fit the unit.</para>
              <figure>
                <title>General arrangement</title>
                <graphic infoEntityIdent="ICN-AE100-00001-A-001-01"/>
              </figure>
            </description>
          </content>
        </dmodule>
        """;

    private const string Fo = "http://www.w3.org/1999/XSL/Format";

    private static IResourceResolver InMemory(Dictionary<string, string> files) =>
        ResourceResolvers.FromDelegate(name => files.TryGetValue(name, out string? text)
            ? new MemoryStream(Encoding.UTF8.GetBytes(text))
            : null);

    // ------------------------------------------------------------------------
    // the resolvers themselves
    // ------------------------------------------------------------------------

    [Fact]
    public void A_directory_resolver_finds_a_name_that_arrives_without_its_extension()
    {
        using var temp = new TempDirectory();
        // S1000D writes an ICN identifier in upper case and the file beside it is
        // named either way, on file systems that may or may not care.
        File.WriteAllBytes(Path.Combine(temp.Path, "ICN-AE100-00001-A-001-01.PNG"), Png());

        IResourceResolver resolver = ResourceResolvers.Directory([temp.Path], [".png", ".jpg"]);

        Assert.NotNull(resolver.LocalPath("ICN-AE100-00001-A-001-01"));
        using Stream? stream = resolver.Open("ICN-AE100-00001-A-001-01");
        Assert.NotNull(stream);
    }

    [Fact]
    public void A_directory_resolver_will_not_be_walked_out_of_its_directory()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "inside.xsl"), "<x/>");

        IResourceResolver resolver = ResourceResolvers.Directory(temp.Path);

        // The name came out of a document. Only its leaf is ever joined to the
        // directory, so a crafted reference resolves to nothing rather than to a
        // file somewhere else.
        Assert.Null(resolver.LocalPath("../../etc/passwd"));
        Assert.NotNull(resolver.LocalPath("elsewhere/inside.xsl"));
    }

    [Fact]
    public void The_first_resolver_that_has_the_name_wins()
    {
        IResourceResolver composed = ResourceResolvers.Compose(
            null,
            InMemory(new Dictionary<string, string> { ["a.xsl"] = "first" }),
            InMemory(new Dictionary<string, string> { ["a.xsl"] = "second", ["b.xsl"] = "only" }));

        Assert.Equal("first", Read(composed, "a.xsl"));
        Assert.Equal("only", Read(composed, "b.xsl"));
        Assert.Null(composed.Open("c.xsl"));
    }

    [Fact]
    public void A_composed_resolver_does_not_hand_out_a_path_to_a_different_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.xsl"), "on disk");

        IResourceResolver composed = ResourceResolvers.Compose(
            InMemory(new Dictionary<string, string> { ["a.xsl"] = "in memory" }),
            ResourceResolvers.Directory(temp.Path));

        // Open answers from memory, so LocalPath must not answer with the path of
        // the file on disk — that is a different file with the same name.
        Assert.Equal("in memory", Read(composed, "a.xsl"));
        Assert.Null(composed.LocalPath("a.xsl"));
    }

    // ------------------------------------------------------------------------
    // an editing stylesheet from a stream
    // ------------------------------------------------------------------------

    [Fact]
    public void An_editing_stylesheet_is_read_from_a_stream()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(HouseStylesheet("FROM A STREAM")));
        var profile = new EditProfile(EditStylesheet.FromStream(stream, "house.xsl"));

        Assert.Equal("FROM A STREAM", EditSession.Parse(Module, profile).Model
            .AllBlocks().First(b => b.Element == "figure").Heading);
    }

    [Fact]
    public void The_stream_is_read_and_closed_before_the_call_returns()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(HouseStylesheet("HELD")));

        EditStylesheet sheet = EditStylesheet.FromStream(stream, "house.xsl");

        // A caller handing over a stream expects to be done with it, and
        // compilation happens later — so the bytes are taken now.
        Assert.Throws<ObjectDisposedException>(() => stream.Position);
        Assert.Equal("HELD", EditSession.Parse(Module, new EditProfile(sheet)).Model
            .AllBlocks().First(b => b.Element == "figure").Heading);
    }

    [Fact]
    public void A_stylesheet_from_a_stream_resolves_its_imports_through_the_resolver()
    {
        IResourceResolver imports = InMemory(new Dictionary<string, string>
        {
            ["house-rules.xsl"] = HouseStylesheet("FROM THE RESOLVER"),
        });

        var entry = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="edit.xsl"/>
              <xsl:import href="house-rules.xsl"/>
            </xsl:stylesheet>
            """));

        var profile = new EditProfile(EditStylesheet.FromStream(entry, "house.xsl", imports));
        EditDocument model = EditSession.Parse(Module, profile).Model;

        // house-rules.xsl came out of the dictionary; edit.xsl is not in it and
        // still came out of the assembly, which is what keeps a house stylesheet
        // ten lines long.
        Assert.Equal("FROM THE RESOLVER", model.AllBlocks().First(b => b.Element == "figure").Heading);
        Assert.Contains(model.AllBlocks(), b => b.Text == "Fit the unit.");
    }

    [Fact]
    public void A_resolver_is_asked_before_the_file_beside_the_stylesheet()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "house-rules.xsl"), HouseStylesheet("FROM DISK"));
        string entry = Path.Combine(temp.Path, "house.xsl");
        File.WriteAllText(entry,
            """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="edit.xsl"/>
              <xsl:import href="house-rules.xsl"/>
            </xsl:stylesheet>
            """);

        IResourceResolver imports = InMemory(new Dictionary<string, string>
        {
            ["house-rules.xsl"] = HouseStylesheet("FROM THE RESOLVER"),
        });

        // A caller who supplied a resolver is saying where their stylesheets live;
        // a same-named file that happens to sit beside this one is not it.
        var profile = new EditProfile(EditStylesheet.FromFile(entry, imports));
        Assert.Equal("FROM THE RESOLVER", EditSession.Parse(Module, profile).Model
            .AllBlocks().First(b => b.Element == "figure").Heading);

        var without = new EditProfile(EditStylesheet.FromFile(entry));
        Assert.Equal("FROM DISK", EditSession.Parse(Module, without).Model
            .AllBlocks().First(b => b.Element == "figure").Heading);
    }

    [Fact]
    public void A_missing_import_says_what_was_not_found()
    {
        var entry = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="nothing-has-this.xsl"/>
            </xsl:stylesheet>
            """));

        EditStylesheet sheet = EditStylesheet.FromStream(entry, "house.xsl");

        Exception e = Assert.ThrowsAny<Exception>(
            () => EditSession.Parse(Module, new EditProfile(sheet)).Model);
        Assert.Contains("nothing-has-this.xsl", Unwrap(e));
    }

    // ------------------------------------------------------------------------
    // a page preview whose stylesheets and illustrations are not files
    // ------------------------------------------------------------------------

    [Fact]
    public void A_presentation_stylesheet_and_its_import_come_out_of_a_resolver()
    {
        var presentation = new EditorPresentation(
            InMemory(PresentationStylesheets()), ResourceResolvers.None);

        Assert.True(presentation.CanPresent("descript"));
        Assert.False(presentation.CanPresent("proced"));

        using PresentationFo fo = presentation.TransformToFo(Module, "descript", "Aeralis AE100");

        // common.xsl is the import; it is what writes the page master, so a page
        // sequence at all proves the import resolved.
        Assert.NotNull(fo.Document.GetElementsByTagName("page-sequence", Fo)[0]);
        Assert.Contains("Fit the unit.", fo.Document.OuterXml);
    }

    [Fact]
    public void An_illustration_that_is_only_bytes_is_laid_out_anyway()
    {
        byte[] png = Png();
        IResourceResolver graphics = ResourceResolvers.FromDelegate(name =>
            name == "ICN-AE100-00001-A-001-01" ? new MemoryStream(png) : null);

        var presentation = new EditorPresentation(InMemory(PresentationStylesheets()), graphics);

        string src;
        using (PresentationFo fo = presentation.TransformToFo(Module, "descript", "Aeralis AE100"))
        {
            // The layout engine resolves an external-graphic by file path and by
            // nothing else — it treats a data: URI exactly as a missing file — so
            // bytes with no path of their own are written out for the layout.
            src = fo.Document.GetElementsByTagName("external-graphic", Fo)
                .OfType<System.Xml.XmlElement>().Single().GetAttribute("src");

            Assert.True(File.Exists(src));
            Assert.Equal(png, File.ReadAllBytes(src));
            Assert.EndsWith(".png", src);   // named for what the bytes are, not for the ICN
        }

        // …and taken away again with the layout that needed them.
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void An_illustration_that_is_already_a_file_is_not_copied()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "ICN-AE100-00001-A-001-01.PNG");
        File.WriteAllBytes(path, Png());

        var presentation = new EditorPresentation(
            InMemory(PresentationStylesheets()),
            ResourceResolvers.Directory([temp.Path], EditorPresentation.GraphicExtensions));

        using PresentationFo fo = presentation.TransformToFo(Module, "descript", "Aeralis AE100");

        Assert.Equal(path, fo.Document.GetElementsByTagName("external-graphic", Fo)
            .OfType<System.Xml.XmlElement>().Single().GetAttribute("src"));
    }

    [Fact]
    public void The_page_renders_with_the_illustration_the_resolver_produced()
    {
        Dictionary<string, string> stylesheets = PresentationStylesheets();
        byte[] png = Png();

        byte[] withImage = new EditorPresentation(InMemory(stylesheets),
            ResourceResolvers.FromDelegate(name =>
                name == "ICN-AE100-00001-A-001-01" ? new MemoryStream(png) : null))
            .RenderPdf(Module, "descript", "Aeralis AE100");

        byte[] without = new EditorPresentation(InMemory(stylesheets), ResourceResolvers.None)
            .RenderPdf(Module, "descript", "Aeralis AE100");

        // Both are PDFs; only one of them has an image in it.
        Assert.Equal("%PDF"u8.ToArray(), withImage[..4]);
        Assert.True(withImage.Length > without.Length,
            $"expected the image to cost bytes: {withImage.Length} vs {without.Length}");
    }

    // ------------------------------------------------------------------------
    // fixtures
    // ------------------------------------------------------------------------

    /// <summary>A house rule for one element, so a projection can be told apart.</summary>
    private static string HouseStylesheet(string heading) =>
        $"""
        <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:import href="edit.xsl"/>

          <xsl:template match="figure">
            <xsl:param name="level" select="0"/>
            <xsl:call-template name="container-block">
              <xsl:with-param name="kind" select="'figure'"/>
              <xsl:with-param name="level" select="$level"/>
              <xsl:with-param name="heading" select="'{heading}'"/>
            </xsl:call-template>
          </xsl:template>
        </xsl:stylesheet>
        """;

    /// <summary>
    /// A presentation stylesheet split the way a house style is: one common.xsl
    /// with the page master in it, and one short stylesheet per object type.
    /// </summary>
    private static Dictionary<string, string> PresentationStylesheets() => new()
    {
        ["common.xsl"] =
            """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                            xmlns:fo="http://www.w3.org/1999/XSL/Format">
              <xsl:param name="publication-title"/>
              <xsl:template name="page">
                <xsl:param name="body"/>
                <fo:root>
                  <fo:layout-master-set>
                    <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                                           margin="20mm">
                      <fo:region-body/>
                    </fo:simple-page-master>
                  </fo:layout-master-set>
                  <fo:page-sequence master-reference="p">
                    <fo:flow flow-name="xsl-region-body">
                      <fo:block font-size="14pt"><xsl:value-of select="$publication-title"/></fo:block>
                      <xsl:copy-of select="$body"/>
                    </fo:flow>
                  </fo:page-sequence>
                </fo:root>
              </xsl:template>
            </xsl:stylesheet>
            """,

        ["descript.xsl"] =
            """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                            xmlns:fo="http://www.w3.org/1999/XSL/Format">
              <xsl:import href="common.xsl"/>
              <xsl:output method="xml" indent="no"/>

              <xsl:template match="/dmodule">
                <xsl:call-template name="page">
                  <xsl:with-param name="body">
                    <xsl:apply-templates select="content"/>
                  </xsl:with-param>
                </xsl:call-template>
              </xsl:template>

              <xsl:template match="para">
                <fo:block font-size="10pt"><xsl:apply-templates/></fo:block>
              </xsl:template>

              <xsl:template match="title">
                <fo:block font-size="10pt" font-weight="bold"><xsl:apply-templates/></fo:block>
              </xsl:template>

              <xsl:template match="graphic[@s1kdResolvedGraphic]">
                <fo:block>
                  <fo:external-graphic src="{@s1kdResolvedGraphic}" content-width="40mm"/>
                </fo:block>
              </xsl:template>

              <xsl:template match="graphic"/>
            </xsl:stylesheet>
            """,
    };

    /// <summary>The smallest real PNG: eight grey pixels, written by our own writer.</summary>
    private static byte[] Png()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "icn.png");
        S1kdTools.Pdf.PngWriter.WriteGray(path, 4, 2, [0, 40, 80, 120, 160, 200, 240, 255]);
        return File.ReadAllBytes(path);
    }

    private static string? Read(IResourceResolver resolver, string name)
    {
        using Stream? stream = resolver.Open(name);
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    }

    private static string Unwrap(Exception e)
    {
        var text = new StringBuilder();
        for (Exception? current = e; current is not null; current = current.InnerException)
        {
            text.Append(current.Message).Append(' ');
        }
        return text.ToString();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() =>
            Path = System.IO.Directory.CreateTempSubdirectory("s1kd-resolver-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
