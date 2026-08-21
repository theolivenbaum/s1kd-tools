# S1kdTools.Editor.Server

The server half of the S1000D WYSIWYG editor, as two calls on an ASP.NET Core
application.

```csharp
builder.Services.AddS1kdEditor(new EditorOptions
{
    CsdbDirectory         = "csdb",           // the objects to edit
    PresentationDirectory = "presentation",   // the XSL-FO page layouts (optional)
});

app.MapS1kdEditor();
```

That is a working editor back-end. The browser half is
[`S1kdTools.Editor`](https://www.nuget.org/packages/S1kdTools.Editor), a Tesserae
component library that speaks exactly this protocol; the editing model underneath
both is [`S1kdTools.Core`](https://www.nuget.org/packages/S1kdTools.Core).

## Why the server holds the document

S1000D is a server-side problem. The projection an editor draws is XSLT over a
DOM, the page preview is an XSL-FO layout, and the business-rule check is a BREX
evaluation — none of which belongs in a browser. So the front-end holds no S1000D
knowledge at all, and everything that knows what a data module is lives behind
these endpoints.

The XML is the document of record. An edit is a command applied to it, and the
model the front-end draws is re-projected from the result — so there is no second
representation that can be right when the file is wrong.

## The endpoints

| | |
|---|---|
| `GET /api/documents` | the CSDB |
| `GET /api/documents/{id}` | open one — the source, the projection, the history |
| `POST /api/documents/{id}/commands` | apply a batch of edits as one undoable step |
| `PUT /api/documents/{id}/xml` | replace the whole source (a code editor's save) |
| `POST …/undo` `…/redo` `…/revert` `…/save` | the session |
| `GET …/check` | well-formedness, business rules, and whether it can be laid out |
| `GET …/pdf` | the page, laid out from what the session holds |
| `GET /api/palette` | the components an author can add, each with the block it projects as |

**Every editing endpoint answers with the whole state rather than a delta.** A
block's path is only valid against the revision it was projected from, so a client
patching a model it already holds would be reasoning about paths the server has
renumbered. A data module's projection is a few tens of kilobytes of JSON; a class
of bug is worth more than that.

## Options

| | |
|---|---|
| `CsdbDirectory` | **required** — the folder of objects, addressed by file name without the extension |
| `WorkingDirectory` | where `save` writes; defaults to `CsdbDirectory` |
| `PresentationDirectory` | the XSL-FO stylesheets, named for the schema they present (`proced.xsl`) |
| `GraphicsDirectory` | where the ICNs are; defaults to `CsdbDirectory` |
| `Profile` | which S1000D dialect to speak — see below |
| `RoutePrefix` | defaults to `/api` |

**No page preview is a supported way to run.** Leave `PresentationDirectory`
unset and `…/pdf` answers 404 with a message saying why, while the check reports
it as a warning rather than an error. An editor without a page is still an editor,
and it is what a project that has not written its house style yet gets.

This package ships no stylesheets of its own, deliberately: how a page looks is a
publishing decision, S1000D does not make it, and neither should a NuGet package.

## Speaking your own dialect

Which parts of an object are editable, and what may be added to one, come from an
`EditProfile` — a stylesheet and a vocabulary:

```csharp
builder.Services.AddS1kdEditor(new EditorOptions
{
    CsdbDirectory = "csdb",
    Profile = new EditProfile(
        EditStylesheet.FromFile("editing/house.xsl"),
        new HouseCatalogue()),
});
```

A house stylesheet is a handful of templates over the shipped one — it imports
`edit.xsl` out of `S1kdTools.Core` and overrides what it disagrees with. See
[`doc/EDITOR.md`](https://github.com/theolivenbaum/s1kd-tools/blob/master/doc/EDITOR.md).

## Sessions

One session per document, shared. That is the honest shape for authoring — a real
system checks a module out to one author — and it is what `AddS1kdEditor`
registers. An application wanting a session per user registers its own
`CsdbLibrary` per scope instead; nothing in the endpoints assumes the singleton.

`EditSession` is not thread-safe, so `CsdbLibrary` holds a lock per document. The
lock is never held across a PDF render.
