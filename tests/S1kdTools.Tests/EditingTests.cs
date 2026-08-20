using S1kdTools.Editing;

namespace S1kdTools.Tests;

/// <summary>
/// The editing projection and the command engine.
///
/// The projection's job is to be a faithful, addressed reading of an object, and
/// the engine's is to write an edit back without collateral damage — so most of
/// what is asserted here is what *survives* an edit, not what changes.
/// </summary>
public class EditingTests
{
    /// <summary>
    /// A procedure with the shapes that make the write path interesting: a
    /// paragraph carrying an inline reference and an emphasis, a nested step
    /// numbered by depth, a warning, and a repeated element with siblings.
    /// </summary>
    private const string Procedure =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="27"
                        subSystemCode="8" subSubSystemCode="1" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="720"
                        infoCodeVariant="A" itemLocationCode="A"/>
                <language languageIsoCode="en" countryIsoCode="GB"/>
                <issueInfo issueNumber="002" inWork="00"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2026" month="05" day="04"/>
                <dmTitle>
                  <techName>Power control unit</techName>
                  <infoName>Installation</infoName>
                </dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="revised">
              <security securityClassification="01"/>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <procedure>
              <commonInfo>
                <para>Do the procedure after <emphasis emphasisType="em02">removal</emphasis>, see <dmRef>
                  <dmRefIdent>
                    <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="27"
                            subSystemCode="8" subSubSystemCode="1" assyCode="00"
                            disassyCode="00" disassyCodeVariant="A" infoCode="520"
                            infoCodeVariant="A" itemLocationCode="A"/>
                  </dmRefIdent>
                  <dmRefAddressItems>
                    <dmTitle><techName>Power control unit</techName><infoName>Removal</infoName></dmTitle>
                  </dmRefAddressItems>
                </dmRef>.</para>
              </commonInfo>
              <mainProcedure>
                <proceduralStep id="first">
                  <title>Prepare</title>
                  <proceduralStep><para>Clean the flange.</para></proceduralStep>
                  <proceduralStep><para>Fit the packing.</para></proceduralStep>
                </proceduralStep>
                <proceduralStep>
                  <warning><warningAndCautionPara>Keep clear.</warningAndCautionPara></warning>
                  <proceduralStep applicRefId="app-1"><para>Lift the unit.</para></proceduralStep>
                </proceduralStep>
              </mainProcedure>
            </procedure>
          </content>
        </dmodule>
        """;

    private static EditSession Open() => EditSession.Parse(Procedure);

    // ------------------------------------------------------------------------
    // projection
    // ------------------------------------------------------------------------

    [Fact]
    public void Projects_the_object_header()
    {
        EditDocument model = Open().Model;

        Assert.Equal("dmodule", model.Root);
        Assert.Equal("Procedure", model.ObjectType);
        Assert.Equal("DMC-EX-A-27-81-00-00A-720A-A", model.Code);
        Assert.Equal("Power control unit — Installation", model.Title);
    }

    [Fact]
    public void Splits_the_object_into_an_ident_section_and_a_content_section()
    {
        EditDocument model = Open().Model;

        Assert.Equal(["ident", "content"], model.Sections.Select(s => s.Key));
    }

    [Fact]
    public void Offers_the_address_as_labelled_fields()
    {
        EditDocument model = Open().Model;
        EditSection ident = model.Sections.First(s => s.Key == "ident");

        EditBlock techName = ident.Blocks.First(b => b.Label == "Technical name");
        Assert.Equal(EditMode.Text, techName.Editable);
        Assert.Equal("Power control unit", techName.Text);

        EditBlock issue = ident.Blocks.First(b => b.Label == "Issue number");
        Assert.Equal(EditMode.Attr, issue.Editable);
        Assert.Equal("issueNumber", issue.AttrName);
        Assert.Equal("002", issue.Value);
    }

    [Fact]
    public void Does_not_mistake_a_referenced_module_title_for_this_one()
    {
        // The paragraph's dmRef carries a dmTitle/techName of its own. A
        // document-wide search would offer it as this module's technical name.
        EditSection ident = Open().Model.Sections.First(s => s.Key == "ident");

        Assert.Single(ident.Blocks, b => b.Label == "Technical name");
        Assert.DoesNotContain(ident.Blocks, b => b.Text == "Removal");
    }

    [Fact]
    public void Numbers_steps_the_way_the_page_will()
    {
        List<EditBlock> steps = [.. Open().Model.AllBlocks().Where(b => b.Kind == "step")];

        Assert.Equal(["1.", "A.", "B.", "2.", "A."], steps.Select(s => s.Label));
    }

    [Fact]
    public void Gives_a_block_the_path_of_the_element_it_came_from()
    {
        EditSession session = Open();
        EditBlock warning = session.Model.AllBlocks().First(b => b.Kind == "warning");

        Assert.Equal(
            "/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[2]/warning[1]",
            warning.Path);
        Assert.Equal(warning.Path, XmlUtils.XPathOf(EditCommands.Resolve(session.Document, warning.Path)));
    }

    [Fact]
    public void Splits_inline_content_into_text_styled_and_atomic_runs()
    {
        EditBlock para = Open().Model.AllBlocks()
            .First(b => b.Kind == "para" && b.Text.StartsWith("Do the procedure"));

        Assert.Collection(para.Runs,
            r => Assert.Equal("Do the procedure after ", r.Text),
            r =>
            {
                Assert.Equal("removal", r.Text);
                Assert.Equal("italic", r.Style);
                Assert.False(r.Atomic);
            },
            r => Assert.Equal(", see ", r.Text),
            r =>
            {
                Assert.True(r.Atomic);
                Assert.Equal("dmRef", r.RefKind);
                Assert.Equal("Power control unit — Removal", r.Text);
                Assert.Equal("DMC-EX-A-27-81-00-00A-520A-A", r.Target);
            },
            r => Assert.Equal(".", r.Text));
    }

    [Fact]
    public void Marks_a_repeated_element_as_removable_and_movable()
    {
        List<EditBlock> steps = [.. Open().Model.AllBlocks().Where(b => b.Kind == "step")];

        Assert.All(steps.Where(s => s.Label is "A." or "B."), s =>
        {
            Assert.True(s.CanDelete);
        });

        // The only child step of the second top-level step has no sibling to swap
        // with, so it can be removed but not reordered.
        EditBlock onlyChild = steps.Last();
        Assert.True(onlyChild.CanDelete);
        Assert.False(onlyChild.CanMove);
    }

    [Fact]
    public void Offers_the_elements_that_may_be_inserted_beside_a_block()
    {
        EditBlock para = Open().Model.AllBlocks()
            .First(b => b.Kind == "para" && b.Text == "Clean the flange.");

        Assert.Contains(para.InsertSiblings, o => o.Element == "warning");
        Assert.Contains(para.InsertSiblings, o => o.Element == "proceduralStep");
    }

    [Fact]
    public void Shows_an_element_it_has_no_template_for_rather_than_dropping_it()
    {
        EditSession session = EditSession.Parse(
            Procedure.Replace("<para>Clean the flange.</para>",
                              "<para>Clean the flange.</para><houseSpecificThing>Kept.</houseSpecificThing>"));

        Assert.Contains(session.Model.AllBlocks(), b => b.Element == "houseSpecificThing");
    }

    // ------------------------------------------------------------------------
    // writing
    // ------------------------------------------------------------------------

    [Fact]
    public void Setting_text_puts_an_untouched_reference_back_verbatim()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks()
            .First(b => b.Kind == "para" && b.Text.StartsWith("Do the procedure"));

        // The author retypes the opening words and leaves the chip alone.
        List<EditRun> runs =
        [
            EditRun.Plain("See "),
            .. para.Runs.Skip(3),
        ];

        session.Apply(new EditCommand { Op = EditOps.SetText, Path = para.Path, Runs = runs });

        string xml = session.Xml;
        Assert.Contains("<techName>Power control unit</techName><infoName>Removal</infoName>", xml);
        Assert.Contains("infoCode=\"520\"", xml);
        Assert.DoesNotContain("<emphasis", xml);
    }

    [Fact]
    public void Setting_text_keeps_an_attribute_the_model_does_not_carry()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks()
            .First(b => b.Kind == "para" && b.Text.StartsWith("Do the procedure"));

        // The emphasis run is retyped but stays italic, so the element it came
        // from is reused — with whatever else it carried.
        List<EditRun> runs = [.. para.Runs];
        runs[1] = new EditRun { Text = "the removal task", Style = "italic", Src = runs[1].Src };

        session.Apply(new EditCommand { Op = EditOps.SetText, Path = para.Path, Runs = runs });

        Assert.Contains("<emphasis emphasisType=\"em02\">the removal task</emphasis>", session.Xml);
    }

    [Fact]
    public void Changing_a_style_writes_the_element_that_carries_it()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks()
            .First(b => b.Kind == "para" && b.Text.StartsWith("Do the procedure"));

        List<EditRun> runs = [.. para.Runs];
        runs[1] = new EditRun { Text = "removal", Style = "superscript", Src = runs[1].Src };

        session.Apply(new EditCommand { Op = EditOps.SetText, Path = para.Path, Runs = runs });

        Assert.Contains("<superScript>removal</superScript>", session.Xml);
        Assert.DoesNotContain("<emphasis", session.Xml);
    }

    [Fact]
    public void Bold_is_written_by_leaving_the_emphasis_type_off()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Clean the flange.");

        session.Apply(new EditCommand
        {
            Op = EditOps.SetText,
            Path = para.Path,
            Runs = [new EditRun { Text = "Clean", Style = "bold" }, EditRun.Plain(" the flange.")],
        });

        Assert.Contains("<emphasis>Clean</emphasis> the flange.", session.Xml);
    }

    [Fact]
    public void Setting_an_attribute_adds_changes_and_removes_it()
    {
        EditSession session = Open();
        string stepPath = session.Model.AllBlocks().First(b => b.Kind == "step" && b.Label == "1.").Path;

        session.Apply(new EditCommand
        {
            Op = EditOps.SetAttr, Path = stepPath, Name = "applicRefId", Value = "app-2",
        });
        Assert.Contains("applicRefId=\"app-2\"", session.Xml);

        session.Apply(new EditCommand
        {
            Op = EditOps.SetAttr, Path = stepPath, Name = "applicRefId", Value = "",
        });
        Assert.DoesNotContain("applicRefId=\"app-2\"", session.Xml);
    }

    [Fact]
    public void Inserting_a_step_creates_one_that_can_be_typed_into()
    {
        EditSession session = Open();
        EditBlock first = session.Model.AllBlocks().First(b => b.Kind == "step" && b.Label == "1.");

        EditDocument model = session.Apply(new EditCommand
        {
            Op = EditOps.Insert,
            Path = first.Path,
            Position = EditPositions.After,
            Element = "proceduralStep",
            Text = "Torque the bolts.",
        });

        List<EditBlock> top = [.. model.AllBlocks().Where(b => b.Kind == "step" && b.Level == first.Level)];
        Assert.Equal(["1.", "2.", "3."], top.Select(s => s.Label));

        EditBlock inserted = top[1];
        Assert.Equal("Torque the bolts.", inserted.Blocks.Single().Text);
        Assert.Equal(EditMode.Text, inserted.Blocks.Single().Editable);
    }

    [Fact]
    public void Inserting_a_warning_brings_the_paragraph_its_schema_requires()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Clean the flange.");

        session.Apply(new EditCommand
        {
            Op = EditOps.Insert, Path = para.Path, Position = EditPositions.Before,
            Element = "warning", Text = "Mind the edge.",
        });

        Assert.Contains(
            "<warning><warningAndCautionPara>Mind the edge.</warningAndCautionPara></warning>",
            session.Xml);
    }

    [Fact]
    public void Moving_a_step_steps_over_the_whitespace_between_siblings()
    {
        EditSession session = Open();
        EditBlock second = session.Model.AllBlocks()
            .First(b => b.Kind == "step" && b.Label == "B.");

        EditDocument model = session.Apply(new EditCommand
        {
            Op = EditOps.Move, Path = second.Path, Direction = "up",
        });

        Assert.Equal("Fit the packing.",
            model.AllBlocks().First(b => b.Kind == "step" && b.Label == "A.").Blocks.Single().Text);
    }

    [Fact]
    public void Moving_past_the_end_is_refused_rather_than_silently_ignored()
    {
        EditSession session = Open();
        EditBlock first = session.Model.AllBlocks().First(b => b.Kind == "step" && b.Label == "1.");

        Assert.Throws<EditCommandException>(() => session.Apply(new EditCommand
        {
            Op = EditOps.Move, Path = first.Path, Direction = "up",
        }));
    }

    [Fact]
    public void Deleting_removes_the_element_and_nothing_else()
    {
        EditSession session = Open();
        EditBlock warning = session.Model.AllBlocks().First(b => b.Kind == "warning");

        session.Apply(new EditCommand { Op = EditOps.Delete, Path = warning.Path });

        Assert.DoesNotContain("Keep clear.", session.Xml);
        Assert.Contains("Lift the unit.", session.Xml);
    }

    [Fact]
    public void A_stale_path_is_refused_and_leaves_the_session_alone()
    {
        EditSession session = Open();
        string before = session.Xml;

        Assert.Throws<EditCommandException>(() => session.Apply(new EditCommand
        {
            Op = EditOps.Delete, Path = "/dmodule[1]/content[1]/procedure[1]/mainProcedure[9]",
        }));

        Assert.Equal(before, session.Xml);
        Assert.Equal(0, session.UndoDepth);
    }

    [Fact]
    public void A_batch_that_fails_part_way_leaves_the_session_alone()
    {
        EditSession session = Open();
        string before = session.Xml;
        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Clean the flange.");

        Assert.Throws<EditCommandException>(() => session.Apply(
        [
            new EditCommand { Op = EditOps.SetText, Path = para.Path, Runs = [EditRun.Plain("Changed.")] },
            new EditCommand { Op = EditOps.Delete, Path = "/dmodule[1]/nonsense[1]" },
        ]));

        Assert.Equal(before, session.Xml);
        Assert.DoesNotContain("Changed.", session.Xml);
    }

    // ------------------------------------------------------------------------
    // history and the source editor
    // ------------------------------------------------------------------------

    [Fact]
    public void Undo_and_redo_walk_the_history()
    {
        EditSession session = Open();
        string before = session.Xml;
        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Clean the flange.");

        session.Apply(new EditCommand
        {
            Op = EditOps.SetText, Path = para.Path, Runs = [EditRun.Plain("Clean the mounting flange.")],
        });

        Assert.Equal("Edit text", session.UndoLabel);
        Assert.Contains("Clean the mounting flange.", session.Xml);

        Assert.True(session.Undo());
        Assert.Equal(before, session.Xml);

        Assert.True(session.Redo());
        Assert.Contains("Clean the mounting flange.", session.Xml);
    }

    [Fact]
    public void A_new_edit_discards_what_had_been_undone()
    {
        EditSession session = Open();
        EditBlock para = session.Model.AllBlocks().First(b => b.Text == "Clean the flange.");

        session.Apply(new EditCommand
        {
            Op = EditOps.SetText, Path = para.Path, Runs = [EditRun.Plain("First.")],
        });
        session.Undo();
        Assert.Equal(1, session.RedoDepth);

        session.Apply(new EditCommand
        {
            Op = EditOps.SetText, Path = para.Path, Runs = [EditRun.Plain("Second.")],
        });

        Assert.Equal(0, session.RedoDepth);
        Assert.Contains("Second.", session.Xml);
    }

    [Fact]
    public void Editing_the_source_reprojects_the_model()
    {
        EditSession session = Open();

        session.SetXml(Procedure.Replace("Power control unit</techName>", "Actuator</techName>"));

        Assert.Equal("Actuator — Installation", session.Model.Title);
        Assert.True(session.Undo());
        Assert.Equal("Power control unit — Installation", session.Model.Title);
    }

    [Fact]
    public void Malformed_source_is_refused_with_the_parser_s_own_message()
    {
        EditSession session = Open();

        EditCommandException error = Assert.Throws<EditCommandException>(
            () => session.SetXml("<dmodule><content></dmodule>"));

        Assert.Contains("Line", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Power control unit — Installation", session.Model.Title);
    }
}
