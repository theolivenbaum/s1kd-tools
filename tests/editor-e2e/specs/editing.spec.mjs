import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE, REFERENCE_PARAGRAPH_PATH } from './_editor.mjs';

/**
 * Editing, and what survives it.
 *
 * The XML is read back through the API rather than from the source pane, so these
 * assert about the file that would be saved rather than about a second view of it.
 * That is the claim the editor exists to make: what the author typed is what is in
 * the document, and nothing else moved.
 */
test.describe('editing a data module', () => {
    test('typing into a block changes the document when the author leaves it', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[1]/para[1]';

        await expect(editor.text(path)).toContainText('Make sure that the mounting flange');

        await editor.retype(path, 'Make sure the flange is clean and undamaged.');

        expect(await editor.xml()).toContain('Make sure the flange is clean and undamaged.');
        expect(await editor.xml()).not.toContain('sealing face');

        const state = await editor.state();
        expect(state.dirty).toBe(true);
        expect(state.undo).toMatchObject({ depth: 1, label: 'Edit text' });
    });

    test('a reference survives a rewrite of the sentence around it', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const paragraph = editor.text(REFERENCE_PARAGRAPH_PATH);

        // Put the caret at the very start and type in front of the chip, then delete
        // the words that followed it — an edit on both sides of a reference the
        // author never touched.
        await paragraph.click();
        await page.keyboard.press('ControlOrMeta+Home');
        await page.keyboard.type('REWRITTEN. ');
        await editor.commit();

        const xml = await editor.xml();
        expect(xml).toContain('REWRITTEN.');

        // The dmRef is the same node it always was: its code, its address items and
        // the title of the module it points at are all still there. Rebuilding it
        // from what the browser knows would have lost every one of them.
        expect(xml).toContain('infoCode="520"');
        expect(xml).toContain('<infoName>Removal</infoName>');
        expect(xml).toContain('<techName>Slat actuation power control unit</techName>');

        // And it is still a chip on the surface, in the same paragraph.
        await expect(paragraph.locator('.s1kd-chip')).toHaveCount(1);
        await expect(paragraph).toContainText('REWRITTEN.');
    });

    test('the toolbar writes an emphasis element around the selection', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[2]/para[1]';

        await editor.text(path).click();
        await page.keyboard.press('ControlOrMeta+Home');

        // Select the first word. Shift+ControlOrMeta+ArrowRight takes the word and
        // the space after it, which is what an author selecting a word gets.
        await page.keyboard.press('Shift+ControlOrMeta+ArrowRight');
        await editor.command('Bold').click();
        await editor.commit();

        // <emphasis> with no emphasisType is S1000D's bold, and writing it by
        // leaving the attribute off is what keeps a document that never used the
        // attribute from acquiring it on its first edit.
        const xml = await editor.xml();
        expect(xml).toMatch(/<emphasis>Install\s*<\/emphasis>/);
        expect(xml).not.toContain('<b>');
    });

    test('Enter makes another block of the same kind', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[4]' +
                     '/proceduralStep[1]/para[1]';

        const before = await page.locator('.s1kd-kind-step').count();

        await editor.text(path).click();
        await page.keyboard.press('End');
        await page.keyboard.press('Enter');
        await page.waitForTimeout(500);

        // The caret lands in the new block, so typing goes there without a click.
        await page.keyboard.type('Record the test result.');
        await editor.commit();

        expect(await editor.xml()).toContain('Record the test result.');
        expect(await page.locator('.s1kd-kind-step').count()).toBe(before);

        // One undo, not two: the commit and the insert went as one batch, because an
        // author who presses Enter and thinks better of it expects one undo.
        const state = await editor.state();
        expect(state.undo.depth).toBe(2);
    });

    test('the gutter inserts, reorders and deletes blocks', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const steps = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]';

        const before = await page.locator('.s1kd-kind-step').count();

        // Insert a step after the first one. It is numbered 2., and everything
        // after it moves down — which is the projection renumbering, not the editor
        // guessing.
        await editor.insert(`${steps}/proceduralStep[1]`, 'Step');
        expect(await page.locator('.s1kd-kind-step').count()).toBe(before + 1);

        const labels = await page.locator('.s1kd-kind-step > .s1kd-label').allTextContents();
        expect(labels.slice(0, 3)).toEqual(['1.', 'A.', 'B.']);
        expect(labels).toContain('5.');

        // Move it up, and what was step 1 is now step 2.
        await editor.gutter(`${steps}/proceduralStep[2]`, 'up');
        await expect(page.locator(`[data-path="${steps}/proceduralStep[2]"]`))
            .toContainText('Prepare for the installation');

        // Delete the empty one, and the module is back to five steps.
        await editor.gutter(`${steps}/proceduralStep[1]`, 'delete');
        await expect(page.locator(`[data-path="${steps}/proceduralStep[1]"]`))
            .toContainText('Prepare for the installation');
    });

    test('the insert menu offers what may go there, and builds it complete', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const para = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[1]/para[1]';

        await editor.block(para).hover();
        await editor.block(para).locator('> .s1kd-gutter > .s1kd-gutter-insert').click();

        // Beside a paragraph in a step, the schema allows all of these. The list is
        // the projection's, not the browser's — which is what lets it be right about
        // a schema this front-end has never been taught.
        const menu = page.locator('.tss-contextmenu-popup');
        for (const label of ['Paragraph', 'Step', 'Warning', 'Caution', 'Note', 'Figure']) {
            await expect(menu.getByText(label, { exact: true })).toBeVisible();
        }

        await menu.getByText('Warning', { exact: true }).click();
        await page.waitForTimeout(500);

        // A <warning/> on its own is invalid the moment it is created, so the
        // paragraph the schema requires comes with it — and it is editable.
        const warning = page.locator(
            '[data-path="/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]' +
            '/proceduralStep[1]/proceduralStep[1]/warning[1]"]');
        await expect(warning).toContainText('WARNING');

        await warning.locator('.s1kd-text').click();
        await page.keyboard.type('Mind the edge.');
        await editor.commit();

        expect(await editor.xml()).toContain(
            '<warning><warningAndCautionPara>Mind the edge.</warningAndCautionPara></warning>');
    });

    test('undo and redo walk the history', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[1]/para[1]';

        await editor.retype(path, 'A changed instruction.');
        expect(await editor.xml()).toContain('A changed instruction.');

        // The button says what it would reverse, rather than leaving the author to
        // remember what they last did.
        await expect(editor.command('Undo')).toHaveText('Undo edit text');
        await editor.command('Undo').click();
        await page.waitForTimeout(400);

        expect(await editor.xml()).not.toContain('A changed instruction.');
        await expect(editor.text(path)).toContainText('Make sure that the mounting flange');

        await editor.command('Redo').click();
        await page.waitForTimeout(400);
        expect(await editor.xml()).toContain('A changed instruction.');
    });

    test('an attribute field writes the attribute', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        const issue = page.locator('.s1kd-section-ident .s1kd-block')
            .filter({ hasText: 'Issue number' }).locator('input');

        await issue.fill('003');
        await editor.commit();

        expect(await editor.xml()).toContain('issueNumber="003"');
    });

    test('saving clears the unsaved mark', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const path = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]/proceduralStep[1]' +
                     '/proceduralStep[1]/para[1]';

        await editor.retype(path, 'Saved text.');
        await expect(page.locator('.s1kd-commandbar')).toContainText('unsaved changes');

        await editor.command('Save').click();
        await page.waitForTimeout(600);

        const state = await editor.state();
        expect(state.dirty).toBe(false);
        expect(state.savedAt).not.toBeNull();
        await expect(page.locator('.s1kd-commandbar')).toContainText('saved');
    });
});
