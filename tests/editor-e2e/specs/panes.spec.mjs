import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE } from './_editor.mjs';

/**
 * The three views of one document, and the check that judges it.
 *
 * Each of these asserts the same thing from a different side: the surface, the
 * source and the page are readings of one document held by one session, not three
 * copies kept in step. An edit made in any of them has to be in the other two
 * without either of them being told.
 */
test.describe('the three views', () => {
    test('an edit on the surface is in the source', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[1]/para[1]';

        await editor.retype(path, 'Text typed on the surface.');
        await editor.show('Source');

        // Monaco renders only the lines in view, so the assertion is on the model
        // rather than on the DOM — what the editor holds, not what it has painted.
        await expect.poll(() => page.evaluate(() =>
            window.monaco?.editor?.getModels?.()[0]?.getValue() ?? ''))
            .toContain('Text typed on the surface.');
    });

    test('hand-edited source becomes the document, and the surface with it', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        await editor.show('Source');

        // Type into the source the way an author does — through Monaco's model, so
        // the change goes through the editor rather than around it.
        await page.evaluate(() => {
            const model = window.monaco.editor.getModels()[0];
            model.setValue(model.getValue().replace(
                '<techName>Slat actuation power control unit</techName>',
                '<techName>Flap actuation power control unit</techName>'));
        });

        const apply = page.getByRole('button', { name: 'Apply to document' });
        await expect(apply).toBeEnabled();
        await apply.click();
        await page.waitForTimeout(700);

        // The document changed, so the projection did: the page heading and the
        // identification field are both reading the same file.
        await expect(page.locator('.s1kd-page-title'))
            .toHaveText('Flap actuation power control unit — Installation');
        expect(await editor.xml()).toContain('Flap actuation power control unit');

        // And it is one undo, like any other edit.
        expect((await editor.state()).undo.label).toBe('Edit source');
    });

    test('malformed source is refused with the parser\'s own words', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const before = await editor.xml();

        await editor.show('Source');
        await page.evaluate(() => {
            window.monaco.editor.getModels()[0].setValue('<dmodule><content></dmodule>');
        });

        await page.getByRole('button', { name: 'Apply to document' }).click();
        await page.waitForTimeout(700);

        // The line and the column are the whole value of the message to an author
        // looking at their own text, so they reach the screen.
        await expect(page.locator('.tss-toast').first()).toContainText(/Line \d+/);

        // And the document is untouched — an author's next move is to fix the line
        // the parser named, which they cannot do if their text has been taken away.
        expect(await editor.xml()).toBe(before);
    });

    test('the page pane lays out what the editor holds', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        await editor.show('Page');

        // A real PDF, drawn into the DOM: pages exist and carry text, which an
        // iframe handed to the browser's plugin could not be asked about at all.
        const pages = page.locator('.s1kd-pdf [data-page-number], .s1kd-pdf .page');
        await expect(pages.first()).toBeVisible({ timeout: 30_000 });
        await expect(page.locator('.s1kd-pdf .s1kd-commandbar')).toContainText(/\d+ pages/);

        await expect.poll(() => page.locator('.s1kd-pdf canvas').count(), { timeout: 30_000 })
            .toBeGreaterThan(0);
    });

    test('the page is re-laid-out after an edit rather than served from cache', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        // Only the document, not pdf.js fetching its own library and worker out of
        // assets/js/pdf/ — which also match a naive /pdf/ filter.
        const renders = [];
        page.on('request', request => {
            if (/\/api\/documents\/.+\/pdf\?/.test(request.url())) renders.push(request.url());
        });

        await editor.show('Page');
        await expect.poll(() => renders.length, { timeout: 30_000 }).toBe(1);

        // Switching away and back with nothing changed costs nothing: the pane does
        // not flash, and the server does not lay out a page nobody has touched.
        await editor.show('Edit');
        await editor.show('Page');
        await page.waitForTimeout(1200);
        expect(renders.length).toBe(1);

        // An edit makes it a different document, and the revision in the URL is what
        // makes the browser fetch it rather than reuse what it has.
        await editor.show('Edit');
        await editor.retype(
            '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
            '/proceduralStep[1]/para[1]',
            'A changed instruction.');
        await editor.show('Page');

        await expect.poll(() => renders.length, { timeout: 30_000 }).toBe(2);
        expect(renders[0]).not.toBe(renders[1]);
    });

    test('the check reports business-rule findings and lands the author on one', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        await editor.command('Check').click();
        await page.waitForTimeout(1500);

        // The shipped module is a revision without a reason for update, which the
        // S1000D default BREX does not allow. A real finding, from a real check.
        const findings = page.locator('.s1kd-finding');
        await expect(findings.first()).toContainText('Business rule');
        await expect(page.locator('.s1kd-finding-error').first()).toBeVisible();

        const report = await (await page.request.get(`/api/documents/${editor.id}/check`)).json();
        expect(report.ok).toBe(false);
        expect(report.brex).toMatch(/^DMC-S1000D-/);
        expect(report.findings.some(f => f.path)).toBe(true);
    });

    test('an edit clears findings that name paths the edit has renumbered', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        await editor.command('Check').click();
        await expect(page.locator('.s1kd-finding').first()).toBeVisible();

        await editor.retype(
            '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
            '/proceduralStep[1]/para[1]',
            'Something else.');

        // The findings named elements in a document that has just been reprojected,
        // so they are taken down rather than left to point somewhere plausible.
        await expect(page.locator('.s1kd-finding')).toHaveCount(0);
    });
});
