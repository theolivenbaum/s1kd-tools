# Editing an S1000D object

`s1kd-tools` can read a CSDB object, write one, filter one and lay one out. This
is about the other thing an author does with one: change it, and see what they
are changing.

There is no upstream C counterpart. The pieces are:

| | |
|---|---|
| `S1kdTools.Core/Resources/editing/edit.xsl` | the **editing projection** — a CSDB object as an addressed tree of editable blocks |
| `S1kdTools.Core/Editing/` | the model, the command engine, the session, the component catalogue |
| `src/S1kdTools.Editor/` | a Tesserae component library: the WYSIWYG surface, the palette, the page preview |
| `samples/editor/` | a Kestrel back-end, a demo front-end, and the CSDB they work on |

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
a stylesheet.** A project with a schema this port has never seen, or a house rule
that a certain title is never retyped, changes `edit.xsl` and gets a different
editor with no C# rebuilt. `EditProjection.Project(doc, stylesheet)` takes the
stylesheet as an argument for exactly that. An element with no template still
appears, through a fall-through that shows it and its children uneditably — so an
unfamiliar object opens rather than failing, and the parts that *are* understood
stay editable.

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

A session is one author's open document and is not thread-safe; a server holding
several serializes access per session.

**A block's path is only valid against the revision it was projected from.**
Inserting a paragraph renumbers the positional predicates of its later siblings,
so read `session.Model` again after every command rather than reusing paths
collected earlier. Every method that changes the document returns the fresh model
for that reason.
