# S1kdTools.Editor

A WYSIWYG editing surface for **S1000D** CSDB objects, built with
[Tesserae](https://github.com/curiosity-ai/tesserae) and compiled to JavaScript by
the Transpose compiler.

```csharp
var client = new EditorClient();                 // same origin as the page
var editor = new S1kdEditor(client);

MountToBody(editor);
await editor.OpenAsync("DMC-AE100-A-27-81-00-00A-720A-A_002-00_EN-GB");
```

| Component | What it is |
|---|---|
| `EditorClient` | The session: open, apply, undo, redo, save, revert, check, and the page's URL. |
| `S1kdEditorSurface` | The editing surface — the object drawn as its page, typed into in place. |
| `S1kdEditor` | The surface plus a command bar: history, save, text formatting, check. |
| `S1kdComponentPalette` | The components this object can take, as cards to drag into it. |
| `S1kdSourcePane` | The same document as XML, in Monaco, writing through the same session. |
| `S1kdPdfPreview` | The same document laid out as the PDF it will be published as. |

The three views are one document, not three. They share one `EditorClient` and so
one server session: an edit made in any of them is in the other two, and none of
them knows the others exist.

## Where the back end is, and how it is called

Nothing here spells a URL. Two seams decide it, and most applications need only
the first:

```csharp
// the endpoints are somewhere else
new EditorClient(routes: new EditorRoutes("/editor-api"));

// somewhere else, and on another origin
new EditorClient(routes: new EditorRoutes("/api", "https://csdb.example.com"));

// the calls are not these calls at all
new EditorClient(api: new MyEditorApi());
```

`EditorRoutes` is **where** each operation lives. It must agree with the back end's
`EditorOptions.RoutePrefix`; both default to `/api`. A back end whose paths are
shaped differently subclasses it and overrides the members it spells differently.

`IEditorApi` is **how** a call is made — one member per operation. Implement it
when the calls have to be different: an editing API that is not these endpoints, an
RPC or socket transport, a host that already has the CSDB in the page, a test
double. The default is `HttpEditorApi`, fetch over `EditorRoutes`; subclass it and
override `SendAsync` to put a bearer token, a retry or a trace header around the
wire without reimplementing anything.

`EditorClient` keeps the session either way — which document is open, what the
server last said, who to tell, and the revision that makes each re-render a
different PDF URL — so an `IEditorApi` of your own gets all of that for free.

## What is and is not in the browser

**No S1000D knowledge is.** The surface draws blocks — a paragraph, a step, a
warning, a labelled field — and posts commands addressed at their paths. Which
elements those are, how they nest, what may be inserted beside one and what the
step numbering comes to are all decided by `S1kdTools.Core`'s editing stylesheet
on the server. So the vocabulary this editor speaks is a stylesheet away from
being a different one, and none of it is compiled into the bundle.

**The document is never mirrored here.** An edit is a command sent to the server
followed by a redraw of the projection that comes back. There is no local model to
fall out of step with the file, which is the failure that makes an in-browser XML
editor untrustworthy: a document that looks right on screen and is not what would
be saved. It costs a round trip per committed edit, and the commit rules are built
around that:

* a block commits when the author **leaves** it, not as they type;
* Enter commits and makes another block of the same kind;
* Escape abandons what is in the block and puts back what the server has;
* anything structural — insert, delete, move, save, check — commits first.

The redraw then happens while nobody is typing, and the caret is put back where
the author moved it.

## What survives an edit

A reference inside a sentence is a **chip**: the author can move it, delete it or
rewrite everything around it, but not retype it. Each run of a block's inline
content carries the position of the element it was made from, and the server puts
that element back rather than rebuilding it — so a `dmRef` comes through an edit
with its address items, its applicability and every attribute this editor has
never heard of intact.

The same mechanism carries `emphasis`, `subScript` and `superScript`: retyping
bold text reuses the element it came from, so an `emphasisType` this model does
not describe is not lost. Changing the *style* is what makes a new element.

Deliberately not preserved: **markup nested inside markup**. Bold holding italic
comes back as one bold run. S1000D allows the nesting; an editor that has to
explain why bold-inside-italic became something else is worse than one that never
offered it.

## The back-end

`EditorClient` speaks to the endpoints in
[`samples/editor/S1kdTools.EditorServer`](../../samples/editor/S1kdTools.EditorServer):

| | |
|---|---|
| `GET /api/documents` | the CSDB |
| `GET /api/documents/{id}` | open one — the source, the projection, the history |
| `POST /api/documents/{id}/commands` | apply a batch of edits as one undoable step |
| `PUT /api/documents/{id}/xml` | replace the whole source (a code editor's save) |
| `POST …/undo`, `…/redo`, `…/revert`, `…/save` | the session |
| `GET …/check` | well-formedness, business rules, and whether it can be laid out |
| `GET …/pdf` | the page, laid out from what the session holds |

Every editing endpoint answers with the **whole** state rather than a delta,
because a block's path is only valid against the revision it was projected from —
inserting a paragraph renumbers its later siblings.

## Styling

`assets/css/s1kd-editor.css` ships in the package and is emitted into the
consuming app's output. It styles the surface as the *page*: the step indentation,
the boxed warnings and the ruled sections of the XSL-FO presentation stylesheets,
in Tesserae's theme colours so it follows the app into dark mode. Everything that
is editor rather than page — the per-block commands — lives in a gutter in the
margin, so revealing it moves no text.
