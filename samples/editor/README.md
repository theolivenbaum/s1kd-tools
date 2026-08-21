# A WYSIWYG editor for S1000D

A data module, open in a browser, drawn as the page it will be published as and
typed into in place — with its source and its rendered PDF beside it, and a rail of
components to drag into it.

```
┌ CSDB ────────┬ Slat actuation power control unit — Installation ──────────────┐
│ · Procedure  │ [ Edit | Source | Page ]                                       │
│ · Descriptive├──────────────┬─────────────────────────────────────────────────┤
│ · Parts data │ COMPONENTS   │ [undo] [redo] [save] [B I x₂ x²] [check]        │
│ · Fault iso  │ ▤ Paragraph  ├─────────────────────────────────────────────────┤
│ · Crew       │ ⚠ Warning    │  PROCEDURE                                      │
│ …            │ ☰ Step       │   1.  Prepare for the installation              │
│              │ ▦ Table  …   │       A.  Make sure that the mounting flange…   │
└──────────────┴──────────────┴─────────────────────────────────────────────────┘
```

| | |
|---|---|
| `S1kdTools.EditorServer/` | the back-end: the editing endpoints, and the host for the front-end |
| `S1kdTools.Editor.App/` | the front-end: the three views, the palette and the CSDB picker |
| `csdb/` | ten data modules and two ICNs — synthetic, [see its README](csdb/README.md) |
| `presentation/` | the XSL-FO stylesheets the page preview lays out with — [see its README](presentation/README.md) |

The components themselves are a library: [`src/S1kdTools.Editor`](../../src/S1kdTools.Editor).
The design — why the XML is the only document and everything else is derived from
it — is in [`doc/EDITOR.md`](../../doc/EDITOR.md).

## Running it

```bash
# once: the compiler, and the package the page preview renders with
dotnet tool install --global Transpose.Compiler
export PATH="$PATH:$HOME/.dotnet/tools"
./samples/editor/pack-tesserae-pdf.sh          # until Tesserae.Pdf is on nuget.org

dotnet build S1kdTools.Editor.slnx             # the front-end
dotnet run --project samples/editor/S1kdTools.EditorServer
```

Then open <http://localhost:5000>.

The server serves the Transpose compiler's output folder directly, so rebuilding
the front-end and refreshing the browser is the whole edit loop — there is no copy
step to have forgotten. It says so plainly, with the command you are missing, if
the front-end has not been built yet.

`--csdb <dir>` points it at a CSDB of your own; `--app <dir>` at a front-end built
somewhere else.

**Saving writes to `samples/out/editor/`,** not back to `csdb/`, so the sample can
be run and saved as often as you like without the checked-in modules drifting. A
real server would write back to the CSDB it read from — `CsdbLibrary` takes the two
directories separately for exactly that reason.

## The three views

They are one document seen three ways, not three documents. All three share one
`EditorClient`, and so share the server session behind it: an edit made in any of
them is in the other two without any of them knowing the others exist. Switching
views commits whatever is being typed first, so none of them ever shows the
document without the sentence the author is in the middle of.

**Edit** is the WYSIWYG surface. Blocks are drawn as the page draws them —
ATA-numbered steps, boxed warnings, ruled notes, parts rows as lines of labelled
fields — and every part of the object that can be typed into is typed into in
place. An edit commits when the author *leaves* a block; Enter commits and makes
another block of the same kind; Escape abandons what is in the block. Per-block
commands (insert, move, delete) live in a gutter in the margin, so revealing them
moves no text.

**Source** is Monaco, through
[Tesserae.Monaco](https://github.com/curiosity-ai/tesserae-monaco). It writes
through the same session: Apply sends the whole text to the server, which parses
it, makes it the document and re-projects — so the surface next door is showing
the hand-edited XML a moment later, and one undo takes it back. Text that is not
well-formed is refused with the parser's line and column, and the author's text is
left exactly where it is.

**Page** is the module laid out by the XSL-FO stylesheets in `presentation/` and
rendered by FOP.Sharp in-process, shown with
[Tesserae.Pdf](https://github.com/curiosity-ai/tesserae-pdf). It lays out what the
*editor* holds, not what is on disk — an author changing a warning wants to see the
warning box move — and only when the pane is actually being looked at.

## The component palette

The rail is not a second list of what S1000D allows. The server derives it from the
same template table the gutter's insert menu uses, and returns a **real projected
block** for each entry, built by the same call an insert command makes. So every
card is drawn by the surface's own renderer, from blocks the editing stylesheet
produced, and shows exactly what dropping it will make — empty, with the
placeholders it will carry.

Where a component may land is read from the projection too: beside a block when the
schema allows it beside, inside when it only allows it inside, and nowhere — the
browser's own refusal cursor — when it allows neither. Clicking a card does the same
thing at the caret, because a palette that only works by dragging does not work with
a keyboard at all.

## The endpoints

| | |
|---|---|
| `GET /api/documents` | the CSDB |
| `GET /api/documents/{id}` | open one — the source, the projection, the history |
| `POST /api/documents/{id}/commands` | apply a batch of edits as one undoable step |
| `PUT /api/documents/{id}/xml` | replace the whole source |
| `POST …/undo` `…/redo` `…/revert` `…/save` | the session |
| `GET …/check` | well-formedness, business rules, and whether it can be laid out |
| `GET …/pdf` | the page, laid out from what the session holds |
| `GET /api/palette` | the components, each with the block it projects as |

Every editing endpoint answers with the **whole** state rather than a delta,
because a block's path is only valid against the revision it was projected from.

One session per document, shared by every browser that opens it. This is a
demonstration server, and two windows on one data module showing each other's edits
is more useful here than per-connection isolation — and it is the honest shape,
since a real authoring system checks a module out to one author.

## Tests

```bash
cd tests/editor-e2e && npm install && npx playwright test
```

Twenty-eight Playwright tests against this server, over this CSDB, through the real
stylesheets and the real layout engine — nothing stubbed. Playwright starts the
server itself; the front-end has to have been built. See
[`tests/editor-e2e`](../../tests/editor-e2e).
