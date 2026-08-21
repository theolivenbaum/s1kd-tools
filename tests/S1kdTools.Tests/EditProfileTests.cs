using System.Xml;
using S1kdTools.Editing;

namespace S1kdTools.Tests;

/// <summary>
/// Extending the editor without forking it.
///
/// The library's claim is that which parts of an object are editable, and what may
/// be added to one, are publishing decisions rather than this library's — so a
/// project brings a stylesheet and a catalogue of its own and gets a different
/// editor. These tests are that claim, made executable: everything here is done
/// the way a consumer would have to do it, against the public API only.
/// </summary>
public class EditProfileTests
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
            <procedure>
              <mainProcedure>
                <proceduralStep>
                  <para>Fit the unit.</para>
                  <note><notePara>Torque values are for a cold assembly.</notePara></note>
                  <houseHazard><warningAndCautionPara>Live circuit.</warningAndCautionPara></houseHazard>
                </proceduralStep>
              </mainProcedure>
            </procedure>
          </content>
        </dmodule>
        """;

    /// <summary>
    /// A house stylesheet: a handful of templates over ours.
    ///
    /// It imports rather than includes, which is what gives its own templates
    /// precedence over the imported ones — an included template colliding with an
    /// existing match at the same priority is an error, and that error is XSLT
    /// saying you meant to import.
    /// </summary>
    private const string HouseStylesheet =
        """
        <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:import href="edit.xsl"/>

          <xsl:template match="houseHazard">
            <xsl:param name="level" select="0"/>
            <xsl:call-template name="container-block">
              <xsl:with-param name="kind" select="'warning'"/>
              <xsl:with-param name="level" select="$level"/>
              <xsl:with-param name="heading" select="'HAZARD'"/>
            </xsl:call-template>
          </xsl:template>

          <xsl:template match="note">
            <xsl:param name="level" select="0"/>
            <xsl:call-template name="container-block">
              <xsl:with-param name="kind" select="'caution'"/>
              <xsl:with-param name="level" select="$level"/>
              <xsl:with-param name="heading" select="'REMARK'"/>
            </xsl:call-template>
          </xsl:template>
        </xsl:stylesheet>
        """;

    /// <summary>A house vocabulary: one element ours has never heard of.</summary>
    private sealed class HouseCatalogue : EditTemplateCatalogue
    {
        private static readonly InsertOption Hazard = new("houseHazard", "Hazard notice", "warning");

        public override IReadOnlyList<InsertOption> SiblingOptions(string parentElement) =>
            parentElement == "proceduralStep"
                ? [.. base.SiblingOptions(parentElement), Hazard]
                : base.SiblingOptions(parentElement);

        public override XmlElement Create(XmlDocument doc, string element, string text = "") =>
            element == "houseHazard"
                ? Wrap(doc, element, Text(doc, "warningAndCautionPara", text))
                : base.Create(doc, element, text);

        public override string Summary(string element) =>
            element == "houseHazard" ? "A hazard notice, as this project writes them."
                                     : base.Summary(element);

        public override IReadOnlyList<PaletteContext> PaletteContexts { get; } =
        [
            new("proceduralStep",
                "<content><procedure><mainProcedure><proceduralStep><para/>{0}</proceduralStep></mainProcedure></procedure></content>"),
        ];
    }

    // ------------------------------------------------------------------------
    // the default profile is still the default
    // ------------------------------------------------------------------------

    [Fact]
    public void An_element_nobody_taught_it_still_appears()
    {
        EditDocument model = EditSession.Parse(Module).Model;

        EditBlock hazard = model.AllBlocks().First(b => b.Element == "houseHazard");
        Assert.Equal("unknown", hazard.Kind);
        Assert.Equal("Live circuit.", hazard.Blocks.Single().Text);
    }

    // ------------------------------------------------------------------------
    // a stylesheet of your own
    // ------------------------------------------------------------------------

    [Fact]
    public void A_house_stylesheet_teaches_it_an_element()
    {
        var profile = new EditProfile(EditStylesheet.FromXml(HouseStylesheet));

        EditBlock hazard = EditSession.Parse(Module, profile).Model
            .AllBlocks().First(b => b.Element == "houseHazard");

        Assert.Equal("warning", hazard.Kind);
        Assert.Equal("HAZARD", hazard.Heading);
    }

    [Fact]
    public void A_house_stylesheet_overrides_a_template_this_library_ships()
    {
        var profile = new EditProfile(EditStylesheet.FromXml(HouseStylesheet));

        EditBlock note = EditSession.Parse(Module, profile).Model
            .AllBlocks().First(b => b.Element == "note");

        // Ours projects a note as kind "note" headed NOTE. Import precedence means
        // the house template wins without either being edited.
        Assert.Equal("caution", note.Kind);
        Assert.Equal("REMARK", note.Heading);
    }

    [Fact]
    public void Everything_the_house_stylesheet_does_not_mention_is_still_ours()
    {
        var profile = new EditProfile(EditStylesheet.FromXml(HouseStylesheet));

        EditDocument model = EditSession.Parse(Module, profile).Model;

        // Ten lines of XSLT bought one new element and one override; the step
        // numbering, the paragraphs and the address fields are all still there.
        Assert.Equal("1.", model.AllBlocks().First(b => b.Kind == "step").Label);
        Assert.Contains(model.AllBlocks(), b => b.Text == "Fit the unit.");
        Assert.Contains(model.Sections.First(s => s.Key == "ident").Blocks,
            b => b.Label == "Technical name" && b.Text == "Actuator");
    }

    [Fact]
    public void A_stylesheet_is_loaded_from_disk_and_resolves_its_imports_from_beside_it()
    {
        string directory = Path.Combine(Path.GetTempPath(), "s1kd-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Split over two files, so the test exercises both halves of the
            // resolver: house-rules.xsl sits beside the entry point, edit.xsl does
            // not exist on disk at all and comes out of the assembly.
            File.WriteAllText(Path.Combine(directory, "house-rules.xsl"),
                """
                <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
                  <xsl:template match="houseHazard">
                    <xsl:param name="level" select="0"/>
                    <xsl:call-template name="container-block">
                      <xsl:with-param name="kind" select="'warning'"/>
                      <xsl:with-param name="level" select="$level"/>
                      <xsl:with-param name="heading" select="'FROM DISK'"/>
                    </xsl:call-template>
                  </xsl:template>
                </xsl:stylesheet>
                """);

            string entry = Path.Combine(directory, "house.xsl");
            File.WriteAllText(entry,
                """
                <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
                  <xsl:import href="edit.xsl"/>
                  <xsl:import href="house-rules.xsl"/>
                </xsl:stylesheet>
                """);

            var profile = new EditProfile(EditStylesheet.FromFile(entry));

            Assert.Equal("FROM DISK", EditSession.Parse(Module, profile).Model
                .AllBlocks().First(b => b.Element == "houseHazard").Heading);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_stylesheet_that_is_not_there_says_so_when_it_is_used()
    {
        // Not when it is named: an EditStylesheet is a description until something
        // projects with it, which is what lets an application build its profile at
        // start-up without touching the disk.
        EditStylesheet missing = EditStylesheet.FromFile("/no/such/house.xsl");

        var error = Assert.Throws<FileNotFoundException>(
            () => EditSession.Parse(Module, new EditProfile(missing)).Model);

        Assert.Contains("house.xsl", error.Message);
    }

    // ------------------------------------------------------------------------
    // a vocabulary of your own
    // ------------------------------------------------------------------------

    [Fact]
    public void A_house_catalogue_offers_its_element_where_it_belongs()
    {
        var profile = new EditProfile(templates: new HouseCatalogue());

        EditBlock para = EditSession.Parse(Module, profile).Model
            .AllBlocks().First(b => b.Text == "Fit the unit.");

        Assert.Contains(para.InsertSiblings, o => o.Element == "houseHazard");

        // And only where it belongs: a paragraph in a description is not a step.
        Assert.DoesNotContain(profile.Templates.SiblingOptions("description"),
            o => o.Element == "houseHazard");
    }

    [Fact]
    public void A_house_catalogue_builds_its_element()
    {
        var profile = new EditProfile(templates: new HouseCatalogue());
        EditSession session = EditSession.Parse(Module, profile);

        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Fit the unit.");

        session.Apply(new EditCommand
        {
            Op = EditOps.Insert,
            Path = para.Path,
            Position = EditPositions.After,
            Element = "houseHazard",
            Text = "Do not touch the busbar.",
        });

        Assert.Contains(
            "<houseHazard><warningAndCautionPara>Do not touch the busbar.</warningAndCautionPara></houseHazard>",
            session.Xml);
    }

    [Fact]
    public void The_palette_picks_up_a_house_element_without_being_told_twice()
    {
        var profile = new EditProfile(EditStylesheet.FromXml(HouseStylesheet), new HouseCatalogue());

        PaletteEntry hazard = EditPalette.Build(profile)
            .Single(e => e.Element == "houseHazard");

        Assert.Equal("Hazard notice", hazard.Label);
        Assert.Equal("A hazard notice, as this project writes them.", hazard.Summary);

        // The preview is a real projection through the house stylesheet — which is
        // what makes the card show what dropping it produces rather than a guess.
        Assert.Equal("HAZARD", hazard.Preview.Heading);
        Assert.Equal("warning", hazard.Preview.Kind);
    }

    [Fact]
    public void Half_a_profile_is_still_a_profile()
    {
        Assert.Same(EditTemplateCatalogue.Default, new EditProfile(EditStylesheet.Default).Templates);
        Assert.Same(EditStylesheet.Default, new EditProfile(templates: new HouseCatalogue()).Stylesheet);

        EditProfile swapped = EditProfile.Default.With(new HouseCatalogue());
        Assert.Same(EditStylesheet.Default, swapped.Stylesheet);
        Assert.IsType<HouseCatalogue>(swapped.Templates);
    }
}
