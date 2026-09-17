import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE } from './_editor.mjs';

/**
 * Where the endpoints are, and how the calls are made, are the host's to decide.
 *
 * The front end used to spell `/api` into eight of its own methods while the back
 * end had a settable `RoutePrefix`. Neither half could say so: move the prefix on
 * the server and every call from the browser answered 404. These tests are the
 * claim that the naming now lives in one place, and that a host can replace the
 * calling logic outright.
 *
 * They drive the client in the page rather than through the UI, because what is
 * under test is the URL it builds and the handler it calls — not what the surface
 * draws once an answer comes back.
 */
test.describe('the back end the front end talks to', () => {
    test('the endpoints can be somewhere else', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        // Nothing is listening there; the assertion is about the request made.
        const asked = [];
        await page.route('**/somewhere-else/**', route => {
            asked.push(new URL(route.request().url()).pathname);
            return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
        });

        await page.evaluate(async () => {
            const { EditorClient, EditorRoutes } = window.S1kdTools.Editor;
            const client = new EditorClient.ctor('', null, null, new EditorRoutes.ctor('/somewhere-else', ''), null);
            await client.ListAsync();
            await client.OpenAsync('DM-1');
            await client.PaletteAsync();
        });

        expect(asked).toEqual([
            '/somewhere-else/documents',
            '/somewhere-else/documents/DM-1',
            '/somewhere-else/documents/DM-1/palette',
        ]);
    });

    test('the calling logic can be the host’s own', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        // An IEditorApi that never goes near the network. A host holding the CSDB
        // in the page, speaking a socket, or standing the editor up in a test
        // supplies one of these and the components do not know the difference.
        //
        // Written here as a plain object because a test cannot compile C#. The
        // member names are asked of the compiler's own output rather than spelled
        // in - Transpose mangles interface members, and which scheme it uses is its
        // business, not this test's. A real host implements the interface in C# and
        // never sees any of this.
        const seen = await page.evaluate(async () => {
            const { EditorClient, HttpEditorApi } = window.S1kdTools.Editor;

            const slot = name => {
                const key = Object.keys(HttpEditorApi.prototype).find(k => k.endsWith('$' + name));
                if (!key) throw new Error('no interface slot for ' + name);
                return key;
            };

            const called = [];
            const state = { id: 'in-memory', xml: '<dmodule/>', model: { sections: [] },
                            undo: { depth: 0 }, redo: { depth: 0 }, dirty: false };

            const api = {};
            api[slot('ListAsync')]    = () => { called.push('list'); return Promise.resolve([{ id: 'in-memory' }]); };
            api[slot('PaletteAsync')] = id => { called.push('palette:' + id); return Promise.resolve([]); };
            api[slot('ReadAsync')]    = id => { called.push('read:' + id); return Promise.resolve(state); };
            api[slot('ApplyAsync')]   = () => { called.push('apply'); return Promise.resolve(state); };
            api[slot('SetXmlAsync')]  = () => Promise.resolve(state);
            api[slot('SessionAsync')] = (id, action) => { called.push(action); return Promise.resolve(state); };
            api[slot('CheckAsync')]   = () => Promise.resolve({ ok: true, findings: [] });
            api[slot('PdfUrl')]       = (id, revision) => 'blob:' + id + '#' + revision;

            const fired = [];
            const client = new EditorClient.ctor('', s => fired.push(s.id), null, null, api);

            await client.ListAsync();
            await client.OpenAsync('in-memory');
            await client.UndoAsync();
            await client.PaletteAsync();

            return { called, fired, pdf: client.PdfUrl(), documentId: client.DocumentId };
        });

        // Every call went to the host's handler, and none of them to the network.
        expect(seen.called).toEqual(['list', 'read:in-memory', 'undo', 'palette:in-memory']);

        // The client still does its own half: it adopts the state, tells its
        // subscribers, and counts revisions so a re-render is a different URL.
        expect(seen.fired).toEqual(['in-memory', 'in-memory']);
        expect(seen.documentId).toBe('in-memory');
        expect(seen.pdf).toBe('blob:in-memory#2');
    });
});
