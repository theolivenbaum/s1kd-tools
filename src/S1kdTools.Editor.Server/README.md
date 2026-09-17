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

## The features, and the endpoints over them

Everything this package does is `EditorOperations`, and every endpoint below is a
single call on it:

```csharp
app.MapGet("/csdb/{id}/edit", (string id, EditorOperations editor) => editor.Read(id));
```

So `MapS1kdEditor` is a convenience, not the interface. An application that wants
its editor reached some other way — its own paths, a controller, an authorization
filter per operation, a queue — takes `EditorOperations` out of DI and maps it,
with nothing to re-derive. `AddS1kdEditor` registers it whether or not you call
`MapS1kdEditor`.

`CsdbLibrary` underneath it is the session store; what `EditorOperations` adds is
the handful of operations that are a composition rather than a call — the palette
for one object, the check, the page.

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
| `GET /api/palette` | the whole catalogue of components, each with the block it projects as |
| `GET /api/documents/{id}/palette` | the same, narrowed to what *this* object can take — what a rail should show |

**The palette is per document.** What may be inserted is a property of the object,
not of the stylesheet: a procedure takes most of the catalogue, a publication module
almost none of it. A rail built from `/api/palette` alone offers a publication module
a warning it can never place, and an author who drags it and sees nothing happen
concludes the editor is broken rather than that the schema is holding.

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
| `PresentationStylesheets` | the same stylesheets, when they are not files — see below |
| `Graphics` | the same illustrations, when they are not files |
| `Profile` | which S1000D dialect to speak — see below |
| `RoutePrefix` | where the endpoints are mapped; defaults to `/api` |

**`RoutePrefix` is a two-sided setting.** The browser half takes its naming from an
`EditorRoutes`, which defaults to the same `/api`; move one and pass the other the
same string:

```csharp
// server
AddS1kdEditor(new EditorOptions { CsdbDirectory = csdb, RoutePrefix = "/editor-api" });

// browser
new EditorClient(routes: new EditorRoutes("/editor-api"));
```

**No page preview is a supported way to run.** Leave `PresentationDirectory`
unset and `…/pdf` answers 404 with a message saying why, while the check reports
it as a warning rather than an error. An editor without a page is still an editor,
and it is what a project that has not written its house style yet gets.

This package ships no stylesheets of its own, deliberately: how a page looks is a
publishing decision, S1000D does not make it, and neither should a NuGet package.

## When the CSDB is not a folder of files

The two directory settings are the common case written short. A CSDB held in a
content management system, an object store or a zip supplies an
`IResourceResolver` instead — `Open(name)` returns a stream or null — and nothing
else changes:

```csharp
builder.Services.AddS1kdEditor(new EditorOptions
{
    CsdbDirectory           = csdb,
    PresentationStylesheets = ResourceResolvers.FromDelegate(store.Open),
    Graphics                = ResourceResolvers.FromDelegate(icns.Open),
});
```

A presentation stylesheet's own `xsl:import` hrefs go back through the same
resolver, so a house style is still one `common.xsl` and thirty short stylesheets
over it. Editing stylesheets take one too — `EditStylesheet.FromStream(…, imports:
…)` — and fall back to the ones embedded in `S1kdTools.Core` for anything the
resolver does not have.

An illustration a resolver can only hand over as bytes is read once and passed to
the XSL-FO engine through its own resolver hook, so nothing is written to disk for
a preview. A resolver that already has the file on disk says so through `LocalPath`
and the bytes are never read into this process at all.

This needs `FOP.Sharp` 26.8.4328 or later, which is where that hook landed.

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
