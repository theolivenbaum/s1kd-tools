# Editing an S1000D object

`s1kd-tools` can read a CSDB object, write one, filter one and lay one out. This
is about the other thing an author does with one: change it, and see what they
are changing.

There is no upstream C counterpart. The pieces are:

| | |
|---|---|
| `S1kdTools.Core/Resources/editing/edit.xsl` | the **editing projection** — a CSDB object as an addressed tree of editable blocks |
| `S1kdTools.Core/Editing/` | the model, the command engine, the session, the component catalogue |
| `src/S1kdTools.Editor.Server/` | the back-end, as two calls on an ASP.NET Core app |
| `src/S1kdTools.Editor/` | a Tesserae component library: the WYSIWYG surface, the palette, the page preview |
| `samples/editor/` | a demo front-end and the CSDB it works on |

Standing one up is three packages and about ten lines:

```csharp
// server
builder.Services.AddS1kdEditor(new EditorOptions { CsdbDirectory = "csdb" });
app.MapS1kdEditor();
```
```csharp
// browser
var editor = new S1kdEditor(new EditorClient());
MountToBody(editor);
await editor.OpenAsync("DMC-….XML");
```

Run it with [`samples/editor/README.md`](../samples/editor/README.md).

## The idea

A structured editor has to answer two questions at once, and they pull against
each other. *What does this say* is answered by showing the author a page. *What
will be saved* is answered by showing them XML. An editor that answers only the
first is a word processor that happens to write angle brackets; one that answers
only the second is an XML editor, which is what technical authors already have and
mostly do not want.

The way out taken here is that **there is only one document, and everything else
is derived from it**:

```
                    ┌──────────────────────────┐
   edit.xsl ───────►│   the block projection   │──► the WYSIWYG surface
                    └──────────────────────────┘    the component palette
   ┌────────────┐          ▲
   │  the XML   │──────────┘
   │ (of record)│──────────┐
   └────────────┘          ▼
        ▲           ┌──────────────────────────┐
        │           │  the presentation XSLT   │──► the page (PDF)
        │           └──────────────────────────┘
        │
   commands ◄── the author
```

Every edit is a command applied to the XML. The projection is then regenerated
from the result, and the surface redrawn from that. Nothing is mirrored, nothing
is reconciled, and there is no second representation that can be right when the
file is wrong. It costs a transform per committed edit — a few milliseconds for a
data module — and buys the one property an authoring tool cannot do without:

> **the editor cannot show something the file does not say.**

## The projection

`edit.xsl` is a sibling of a presentation stylesheet, not a replacement for one.
Both read a CSDB object; one emits XSL-FO, the other emits blocks:

```xml
<block path="/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[2]"
       element="proceduralStep" kind="step" label="2." level="3" editable="none"
       canDelete="1" canMove="1">
  <blocks>
    <block … kind="warning" heading="WARNING"> … </block>
    <block … kind="para" editable="text">
      <runs>
        <run text="Install the four attachment bolts. See "/>
        <run kind="element" atomic="1" element="internalRef" refKind="internalRef"
             src="2" text="Bolt tightening sequence" target="fig-torque"/>
        <run text="."/>
      </runs>
    </block>
  </blocks>
</block>
```

Three things are load-bearing:

**`@path` is generated exactly as `XmlUtils.XPathOf` generates one.** The
stylesheet writes these and the command engine resolves them; a disagreement
between the two would not be cosmetic, it would apply an edit to the wrong
element.

**A block's `@label` is the number the page will print.** Step numbering is the
ATA scheme the presentation stylesheets use — `1.` / `A.` / `(1)` / `(a)` — worked
out from depth, so the editor and the PDF agree about what step 2.B is.

**Inline content is runs, not text.** A run is editable text with at most one
emphasis, or an *atomic* element the author sees as a chip and cannot retype.
Each run that came from an element records that element's position in `@src`.

**Which parts of an object are editable is a publishing decision, and it lives in
a stylesheet.** An element with no template still appears, through a fall-through
that shows it and its children — so an unfamiliar object opens rather than failing.
Measured over the 397 CSDB objects in `samples/datasets`, spanning Issues 4.0, 4.2
and 5.0 and a dozen schemas this projection was never written for, **99.9% of the
authorable text lands in an editable block** with no adaptation at all. What the
fall-through cannot give you is the presentation intelligence: step numbering,
boxed warnings, and headings better than the element name with its camel humps
opened out.

## Speaking your own dialect

Those are exactly the things a project overrides, and doing so is a stylesheet and
a profile rather than a fork.

An `EditProfile` is the pair "how an object is projected" and "what may be added to
one" — one decision seen from two sides, so they travel together:

```csharp
var profile = new EditProfile(
    EditStylesheet.FromFile("editing/house.xsl"),
    new HouseCatalogue());

var session = EditSession.Open("DMC-….XML", profile);
var palette = EditPalette.Build(profile);
```

**A house stylesheet starts from ours.** However it is loaded — a file, a string, a
stream, a resource — its `xsl:import` hrefs resolve against its own directory first
and then against `S1kdTools.Core`'s embedded stylesheets, so it is a handful of
templates rather than a copy of a thousand lines:

```xml
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:import href="edit.xsl"/>

  <!-- teach it an element we have never heard of -->
  <xsl:template match="houseHazard">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'warning'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading" select="'HAZARD'"/>
    </xsl:call-template>
  </xsl:template>
</xsl:stylesheet>
```

Use `xsl:import` to *override* a template ours already has — import precedence is
what makes yours win — and `xsl:include` to add matches for elements it does not.
An included template that collides with an existing one at the same priority is an
error, which is XSLT telling you that you meant to import.

**A house vocabulary is a subclass.** `EditTemplateCatalogue` says what may be
inserted where, what a new element is made of, and what the palette says about it.
Override the parts you disagree with and call `base` for the rest:

```csharp
sealed class HouseCatalogue : EditTemplateCatalogue
{
    static readonly InsertOption Hazard = new("houseHazard", "Hazard notice", "warning");

    public override IReadOnlyList<InsertOption> SiblingOptions(string parent) =>
        parent == "proceduralStep"
            ? [.. base.SiblingOptions(parent), Hazard]
            : base.SiblingOptions(parent);

    public override XmlElement Create(XmlDocument doc, string element, string text = "") =>
        element == "houseHazard"
            ? Wrap(doc, element, Text(doc, "warningAndCautionPara", text))
            : base.Create(doc, element, text);
}
```

That one element is now in the gutter's insert menu, in the drag-and-drop target
rules, and on a component-palette card whose preview is a real projection through
your stylesheet — without being named anywhere else.
`tests/S1kdTools.Tests/EditProfileTests.cs` does all of the above through the
public API only, the way a consumer has to.

## When the CSDB is not a folder of files

Everything above assumes the stylesheets and the illustrations are on disk, because
in a small CSDB they are. In a publishing organisation they are as likely to be rows
in a content management system, objects in a bucket, or entries in a zip. That is a
resolver, not a fork:

```csharp
public interface IResourceResolver
{
    Stream? Open(string name);          // null when this resolver does not have it
    string? LocalPath(string name) => null;
}
```

`ResourceResolvers` has the ones you rarely need to write yourself — `Directory`,
`Embedded`, `FromDelegate`, `Compose` (first one that has the name wins) and `None`.
The name handed to a resolver came out of a document, so treat it as untrusted; the
directory resolver only ever joins its leaf to a folder, which is why
`../../etc/passwd` resolves to nothing.

There are three places a name turns into bytes, and each takes one.

**An editing stylesheet, and what it imports.** Every factory takes an optional
`imports` resolver, and there is a `FromStream` for a stylesheet that is not a file
at all:

```csharp
var sheet = EditStylesheet.FromStream(
    store.Open("house.xsl"), "house.xsl",
    imports: ResourceResolvers.FromDelegate(store.Open));
```

An href is asked of your resolver first, then of the directory the stylesheet came
from, then of this assembly — so `house-rules.xsl` comes out of your store and
`edit.xsl` still comes out of the package. The stream is read and closed before the
call returns, not at first projection: a caller handing over a stream expects to be
done with it.

**The presentation stylesheets.** `EditorOptions.PresentationStylesheets` replaces
`PresentationDirectory`, and a stylesheet's own `xsl:import` hrefs go back through
the same resolver — which is what keeps a house style one `common.xsl` and thirty
short stylesheets over it, wherever those thirty live.

**The illustrations.** `EditorOptions.Graphics` replaces `GraphicsDirectory` and
answers to an ICN identifier.

```csharp
builder.Services.AddS1kdEditor(new EditorOptions
{
    CsdbDirectory = csdb,
    PresentationStylesheets = ResourceResolvers.FromDelegate(store.Open),
    Graphics = ResourceResolvers.FromDelegate(icns.Open),
});
```

`LocalPath` is the one thing on the interface that is not obviously necessary, and
it is there for a measured reason: the XSL-FO layout engine resolves an
`external-graphic` by file path and by nothing else — it treats a `data:` URI
exactly as it treats a missing file. So an illustration that a resolver can only
hand over as bytes is written to a temporary file for the length of one layout and
deleted with it, and a resolver that already has the file on disk says so through
`LocalPath` and nothing is copied. That is also why `TransformToFo` returns a
disposable `PresentationFo` rather than a bare `XmlDocument`.

`tests/S1kdTools.Tests/ResourceResolverTests.cs` runs an editing stylesheet, its
import, a presentation stylesheet, its import and an illustration entirely out of
memory, and gets the same editor and the same page back.

## Writing back

`EditCommands` is the half that must not be clever. The projection can afford to
simplify; a view that shows a little less than the object holds costs nothing.
Writing cannot: an edit that silently drops an attribute loses data nothing
downstream can recover.

So the write path is built on one rule:

> **an element the author did not retype is put back, not recreated.**

`setText` snapshots the block's child elements before removing them, and the
`@src` on each run says which one it came from. An untouched `dmRef` is moved back
into place — the same node, never serialized and re-parsed — so it survives an
edit to the sentence around it with its address items, its applicability and every
attribute this port has never heard of intact. Styled runs are the same story:
retyping bold text reuses the `emphasis` element it came from, so an
`emphasisType` the model does not describe is not lost. Changing the *style* is
what makes a new element.

Deliberately not preserved: markup nested inside markup. Bold holding italic comes
back as one bold run. S1000D allows the nesting; an editor that has to explain why
bold-inside-italic became something else is worse than one that never offered it.

The commands are `setText`, `setAttr`, `insert`, `delete` and `move`. A batch is
applied to a copy and swapped in only if every command lands, because a
half-applied batch is a document nobody asked for and one the author cannot undo
their way out of.

## Validity

Nothing here refuses an edit. A data module halfway through being written is
invalid nearly all the time, and an editor that will not let an author leave a
paragraph until it validates is an editor they will leave instead.

Instead the document is *checked*, on demand, and the findings are reported:

* **is it well-formed** — the parser's own message, with its line and column;
* **does it follow the business rules** — `BrexCheck.CheckDefault` against the
  S1000D default BREX for the module's own issue, so no project BREX is needed;
* **can it be presented** — the FO transform is run and its failure, if any, is
  reported, because an author who cannot see the page needs to know it is the
  module and not the preview.

Every BREX finding carries the XPath of the element it is about, in the same shape
a block carries — which is what turns `objectPath /dmodule[1]/…` from something the
author has to decode into somewhere they can click.

`EditTemplates` is the editor's own opinion about what a new element is made of:
a `warning` arrives with the `warningAndCautionPara` its schema requires, because
an empty one is invalid the moment it is created and the author should not have to
fix that through a second menu. The table it keys on is also what `insertSiblings`
and `insertChildren` are computed from, so the gutter menu, the drag-and-drop
target rules and the component palette are all reading one list.

## Using it from C#

```csharp
using S1kdTools.Editing;

var session = EditSession.Open("DMC-AE100-A-27-81-00-00A-720A-A_002-00_EN-GB.XML");

EditBlock step = session.Model.AllBlocks().First(b => b.Kind == "step" && b.Label == "2.");

session.Apply(EditCommand.Insert(step.Path, EditPositions.LastChild, "warning"));
session.Apply(new EditCommand
{
    Op = EditOps.SetText,
    Path = step.Blocks[0].Path,
    Runs = [EditRun.Plain("Keep clear while the unit is lifted.")],
});

session.Undo();
File.WriteAllText("out.XML", session.Xml);
```

`EditSession.Open(path, profile)` takes a profile too; omitting it is
`EditProfile.Default`, the stylesheet and vocabulary this library ships.

A session is one author's open document and is not thread-safe; a server holding
several serializes access per session — which is what `CsdbLibrary` in
`S1kdTools.Editor.Server` does.

**A block's path is only valid against the revision it was projected from.**
Inserting a paragraph renumbers the positional predicates of its later siblings,
so read `session.Model` again after every command rather than reusing paths
collected earlier. Every method that changes the document returns the fresh model
for that reason.
