# The editor, end to end

Playwright against the sample editor: a real Kestrel server over the real CSDB, the
real editing stylesheet, and the real XSL-FO layout engine.

```bash
npm install
npx playwright test
```

The config starts the server (`webServer`), so this is the whole story — provided
the front-end has been built, which it is not by `npm install`:

```bash
dotnet tool install --global Transpose.Compiler
dotnet build ../../S1kdTools.Editor.slnx
```

## Why not unit tests

There are unit tests, in `tests/S1kdTools.Tests/EditingTests.cs`, and they prove the
projection and the command engine are right about XML. What they cannot reach is the
claim the editor actually makes — that what the author sees, what would be saved,
and what would be printed are one document. That claim only exists once a browser, a
server and a layout engine are all in the room.

So these tests read the XML back through the API rather than off the screen, and
assert about the file that would be saved: that a reference survives a rewrite of
the sentence around it, that a palette card's preview and the block a drop makes are
the same block, that the page pane re-lays-out after an edit and not before.

| | |
|---|---|
| `specs/surface.spec.mjs` | the projection: step numbering, boxed warnings, reference chips, the address as fields |
| `specs/editing.spec.mjs` | the round trip: typing, formatting, insert/move/delete, history, saving |
| `specs/palette.spec.mjs` | the component rail: what it offers, what it promises, where a drop may land |
| `specs/panes.spec.mjs` | the three views over one session, and the check |
| `specs/_editor.mjs` | the gestures, named as the author's actions rather than as clicks |

## One worker

The server keeps one editing session per data module and these tests edit them, so
two workers would be two authors typing into one document — a thing the server
supports and a thing that makes a test suite meaningless. Each test reverts the
module it is about before opening it, which is what keeps them order-independent
without a server each.

## Two traps worth knowing

**Drag and drop is not mouse events.** HTML5 drag and drop is not driven by
synthetic `mousedown`/`mousemove`/`mouseup`, so a hand-rolled drag presses the card
and releases it over the target without a single drag event having fired — and then
passes or fails for reasons that have nothing to do with the editor. `locator.dragTo`
is the one that works.

**Chromium does not recompute `:hover` under a stationary pointer.** Every command
redraws the page, so hovering the same spot twice in a row is a no-op move and the
per-block gutter stays hidden. A person moves the mouse between commands; a test has
to be told to — see `Editor.reach`.
