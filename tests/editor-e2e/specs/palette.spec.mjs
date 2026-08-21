import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE } from './_editor.mjs';

/**
 * The component palette: what it offers, what it promises, and what dropping one
 * actually does.
 *
 * The claim under test is that the rail is not a second list. Everything on it —
 * which components exist, what each is called, and the shape shown behind it — is
 * derived on the server from the same templates and the same editing stylesheet
 * that an insert command goes through. So these tests assert the *correspondence*
 * as much as the behaviour: the preview a card shows and the block a drop makes
 * are the same block.
 */
test.describe('the component palette', () => {
    test('offers the components the server derives, not a list of its own', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        const catalogue = await (await page.request.get('/api/palette')).json();
        expect(catalogue.length).toBeGreaterThan(10);

        await expect(page.locator('.s1kd-palette-card')).toHaveCount(catalogue.length);

        for (const entry of catalogue) {
            const card = page.locator(`.s1kd-palette-card[data-element="${entry.element}"]`);
            await expect(card).toHaveAttribute('aria-label', `Add ${entry.label}`);
            await expect(card).toContainText(entry.label);
        }
    });

    test('shows the block a component projects as, drawn by the surface renderer', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        await page.locator('.s1kd-palette-card[data-element="warning"]').hover();

        // The preview is the projection: the same boxed WARNING the page draws,
        // built from the same blocks by the same renderer — and empty, because that
        // is what dropping it produces.
        const preview = page.locator('.s1kd-palette-preview');
        await expect(preview.locator('.s1kd-block.s1kd-kind-warning')).toBeVisible();
        await expect(preview).toContainText('WARNING');
        await expect(preview.locator('.s1kd-text')).toHaveText('');
        await expect(preview.locator('.s1kd-text')).toHaveAttribute('data-placeholder', 'Paragraph text');

        // A preview is a picture, not a place: nothing in it is addressed or typed
        // into, so it cannot be edited by mistake.
        await expect(preview.locator('[data-path]')).toHaveCount(0);
        await expect(preview.locator('[contenteditable="true"]')).toHaveCount(0);
    });

    test('drops a component where the insertion line said it would go', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const step = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]' +
                     '/proceduralStep[1]/proceduralStep[1]';

        await editor.drag('Warning', `${step}/para[1]`, 'top');

        // Dropped above the paragraph, so it is the step's first child.
        await expect(page.locator(`[data-path="${step}/warning[1]"]`)).toContainText('WARNING');

        const xml = await editor.xml();
        expect(xml).toContain('<warning><warningAndCautionPara /></warning>');

        const state = await editor.state();
        expect(state.undo.label).toBe('Insert warning');
    });

    test('drops below the block when the pointer is in its lower half', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const step = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]' +
                     '/proceduralStep[1]/proceduralStep[1]';

        await editor.drag('Note', `${step}/para[1]`, 'bottom');

        // Below the paragraph rather than above it: the note follows it.
        await expect(page.locator(`[data-path="${step}/note[1]"]`)).toContainText('NOTE');
        await expect(page.locator(`[data-path="${step}/para[1]"]`))
            .toContainText('Make sure that the mounting flange');
    });

    test('refuses a component the schema does not allow there', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        // A table row belongs in a table body and nowhere else. Dropping one on a
        // paragraph must do nothing at all — not insert it, not mark the document
        // dirty, not put a step on the undo stack.
        const before = await editor.xml();
        await editor.drag('Table row',
            '/dmodule[1]/content[1]/procedure[1]/commonInfo[1]/para[1]');

        expect(await editor.xml()).toBe(before);
        expect((await editor.state()).undo.depth).toBe(0);
    });

    test('adds a component by click, for anyone not using a mouse', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const step = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]' +
                     '/proceduralStep[1]/proceduralStep[2]';

        // Put the caret somewhere, then press the card: the component lands after
        // the block the author was last in.
        await editor.text(`${step}/para[1]`).click();
        await page.keyboard.press('End');
        await editor.card('Caution').click();
        await page.waitForTimeout(700);

        await expect(page.locator(`[data-path="${step}/caution[1]"]`)).toContainText('CAUTION');
    });

    test('a dropped component is one undo away', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);
        const step = '/dmodule[1]/content[1]/procedure[1]/mainProcedure[1]' +
                     '/proceduralStep[1]/proceduralStep[1]';

        await editor.drag('Figure', `${step}/para[1]`, 'bottom');
        await expect(page.locator(`[data-path="${step}/figure[1]"]`)).toBeVisible();

        await editor.command('Undo').click();
        await page.waitForTimeout(500);

        await expect(page.locator(`[data-path="${step}/figure[1]"]`)).toHaveCount(0);
        expect((await editor.state()).undo.depth).toBe(0);
    });
});
