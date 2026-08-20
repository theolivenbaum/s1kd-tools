import { defineConfig, devices } from '@playwright/test';

/**
 * The editor, end to end: a real Kestrel server over the real CSDB, the real
 * editing stylesheet, and the real XSL-FO layout engine. Nothing is stubbed.
 *
 * That is the point of testing it this way rather than with unit tests alone.
 * The unit tests in `tests/S1kdTools.Tests` prove the projection and the command
 * engine are right about XML; what they cannot reach is the claim this editor
 * actually makes — that what an author sees, what would be saved, and what would
 * be printed are the same document. That claim only exists once a browser, a
 * server and a layout engine are all in the room.
 *
 * `webServer` builds and starts the server, so `npm test` from a clean checkout
 * is the whole story — provided the front-end has been built (see README.md);
 * the server serves the compiler's output folder directly and says so plainly
 * when it is not there.
 */
const port = Number(process.env.S1KD_EDITOR_PORT ?? 5199);

export default defineConfig({
    testDir: './specs',
    fullyParallel: false,

    // One worker. The server keeps one editing session per data module and the
    // tests edit them, so two workers would be two authors typing into one
    // document — which is a thing the server supports and a thing that makes a
    // test suite meaningless.
    workers: 1,

    forbidOnly: !!process.env.CI,
    retries: 0,
    reporter: process.env.CI ? [['github'], ['list']] : [['list']],

    use: {
        baseURL: `http://127.0.0.1:${port}`,
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
    },

    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    ],

    webServer: {
        command: `dotnet run --project ../../samples/editor/S1kdTools.EditorServer -- --urls http://127.0.0.1:${port}`,
        url: `http://127.0.0.1:${port}/api/documents`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        stdout: 'ignore',
        stderr: 'pipe',
    },
});
