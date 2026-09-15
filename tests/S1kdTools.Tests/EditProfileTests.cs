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

    // ------------------------------------------------------------------------
    // the palette is a property of the object, not of the stylesheet
    // ------------------------------------------------------------------------

    /// <summary>
    /// A publication module: its content is entries and references, and almost
    /// nothing the vocabulary can build belongs in it.
    /// </summary>
    private const string PublicationModule =
        """
        <pm>
          <identAndStatusSection>
            <pmAddress>
              <pmAddressItems><pmTitle>Aeralis AE100 maintenance</pmTitle></pmAddressItems>
            </pmAddress>
          </identAndStatusSection>
          <content>
            <pmEntry>
              <pmEntryTitle>Slat actuation</pmEntryTitle>
              <dmRef><dmRefIdent><dmCode modelIdentCode="AE100"/></dmRefIdent></dmRef>
            </pmEntry>
          </content>
        </pm>
        """;

    [Fact]
    public void The_palette_for_an_object_is_what_that_object_can_take()
    {
        IReadOnlyList<PaletteEntry> whole = EditPalette.Build();

        EditDocument procedure = EditSession.Parse(Module).Model;
        EditDocument publication = EditSession.Parse(PublicationModule).Model;

        IReadOnlyList<PaletteEntry> inProcedure = EditPalette.Build(procedure);
        IReadOnlyList<PaletteEntry> inPublication = EditPalette.Build(publication);

        // The catalogue is everything the vocabulary knows how to build. What a
        // given object accepts is a much smaller thing, and a rail that offered the
        // whole catalogue to a publication module would refuse nearly every drop
        // without ever saying why - which an author reads as a broken editor rather
        // than as the schema doing its job.
        Assert.True(inProcedure.Count > inPublication.Count,
            $"a procedure should take more than a publication module: " +
            $"{inProcedure.Count} vs {inPublication.Count}");
        Assert.True(inProcedure.Count < whole.Count);

        // Whatever is offered, every entry can land somewhere in the object.
        foreach ((EditDocument document, IReadOnlyList<PaletteEntry> offered) in
                 new[] { (procedure, inProcedure), (publication, inPublication) })
        {
            HashSet<string> accepted =
            [
                .. document.AllBlocks()
                           .SelectMany(b => b.InsertSiblings.Concat(b.InsertChildren))
                           .Select(o => o.Element),
            ];

            Assert.NotEmpty(offered);
            Assert.All(offered, entry => Assert.Contains(entry.Element, accepted));
        }

        // Nothing is invented: it is the one catalogue, narrowed.
        Assert.All(inPublication, entry => Assert.Contains(whole, w => w.Element == entry.Element));
    }

    [Fact]
    public void A_house_element_is_offered_only_where_it_fits()
    {
        var profile = new EditProfile(EditStylesheet.FromXml(HouseStylesheet), new HouseCatalogue());

        // The house catalogue puts houseHazard beside a proceduralStep and nowhere
        // else, so the module with steps offers it and the publication module does
        // not - without either of them being named in the filter.
        Assert.Contains(EditPalette.Build(EditSession.Parse(Module, profile).Model, profile),
            e => e.Element == "houseHazard");

        Assert.DoesNotContain(
            EditPalette.Build(EditSession.Parse(PublicationModule, profile).Model, profile),
            e => e.Element == "houseHazard");
    }

    // ------------------------------------------------------------------------
    // what may go beside something the catalogue has never heard of
    // ------------------------------------------------------------------------

    /// <summary>
    /// Two containers S1000D has and this catalogue does not name: one holding a
    /// single child, one holding several.
    /// </summary>
    private const string UnknownContainers =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmAddressItems>
                <dmTitle><techName>Actuator</techName></dmTitle>
              </dmAddressItems>
            </dmAddress>
          </identAndStatusSection>
          <content>
            <description>
              <table>
                <tgroup cols="2">
                  <tbody>
                    <row><entry>Torque</entry><entry>12 Nm</entry></row>
                  </tbody>
                </tgroup>
              </table>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void An_unknown_container_holding_one_child_offers_nothing_beside_it()
    {
        EditDocument model = EditSession.Parse(UnknownContainers).Model;

        // <dmTitle> is not in the catalogue's table, and this one holds a single
        // <techName>. The catalogue used to fall through to "Paragraph", so the
        // author was invited to put a <para> inside a <dmTitle> - which the schema
        // forbids, and which the projection then could not draw, so pressing insert
        // appeared to do nothing at all. Measured over the sample CSDB that guess
        // was wrong 388 times and right 38.
        EditBlock techName = model.AllBlocks().First(b => b.Element == "techName");
        Assert.Empty(techName.InsertSiblings);
    }

    [Fact]
    public void An_unknown_container_holding_several_offers_another_of_the_same()
    {
        EditDocument model = EditSession.Parse(UnknownContainers).Model;

        // <row> is not in the table either, but this one holds two <entry>s, and a
        // document holding two of something has said what no guess could: that a
        // third is allowed. So the offer is another entry - not a paragraph.
        EditBlock entry = model.AllBlocks().First(b => b.Element == "entry");
        EditTemplateCatalogue.InsertOption option = Assert.Single(entry.InsertSiblings);

        Assert.Equal("entry", option.Element);
        Assert.Equal("Entry", option.Label);
    }

    [Fact]
    public void A_container_the_catalogue_does_name_is_unaffected()
    {
        EditDocument model = EditSession.Parse(UnknownContainers).Model;

        // The observation only applies where the catalogue is silent. <tbody> it
        // knows about, so a row inside one is offered what the vocabulary says
        // rather than what the document happens to contain.
        EditBlock row = model.AllBlocks().First(b => b.Element == "row");
        Assert.Contains(row.InsertSiblings, o => o.Element == "row" && o.Label == "Table row");
    }

    [Fact]
    public void An_element_name_becomes_a_label_a_person_can_read()
    {
        var catalogue = new EditTemplateCatalogue();

        Assert.Equal("Check list item", catalogue.Another("checkListItem", "unknown").Label);
        Assert.Equal("Entry", catalogue.Another("entry", "unknown").Label);
        Assert.Equal("Catalog seq number", catalogue.Another("catalogSeqNumber", "unknown").Label);
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
