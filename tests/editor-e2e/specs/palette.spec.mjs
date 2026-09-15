import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE, FRONTMATTER } from './_editor.mjs';

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

        const offered = await (await page.request.get(`/api/documents/${PROCEDURE}/palette`)).json();
        expect(offered.length).toBeGreaterThan(10);

        await expect(page.locator('.s1kd-palette-card')).toHaveCount(offered.length);

        for (const entry of offered) {
            const card = page.locator(`.s1kd-palette-card[data-element="${entry.element}"]`);
            await expect(card).toHaveAttribute('aria-label', `Add ${entry.label}`);
            await expect(card).toContainText(entry.label);
        }
    });

    test('offers what this object can take, not the whole vocabulary', async ({ page }) => {
        // What may be inserted is a property of the object, not of the stylesheet.
        // A procedure takes most of the vocabulary; a front matter module, whose
        // content the schema fixes, takes almost none of it. A rail that showed the
        // whole catalogue either way would refuse nearly every drop in the second
        // without ever saying why, which reads as a broken editor.
        const whole = await (await page.request.get('/api/palette')).json();

        await openEditor(page, PROCEDURE);
        const inProcedure = await page.locator('.s1kd-palette-card').evaluateAll(
            els => els.map(e => e.getAttribute('data-element')));

        await openEditor(page, FRONTMATTER);
        const inFrontMatter = await page.locator('.s1kd-palette-card').evaluateAll(
            els => els.map(e => e.getAttribute('data-element')));

        // Both are drawn from the one catalogue, and neither is all of it.
        expect(inProcedure.length).toBeGreaterThan(inFrontMatter.length);
        expect(inProcedure.length).toBeLessThan(whole.length);
        for (const element of [...inProcedure, ...inFrontMatter]) {
            expect(whole.map(e => e.element)).toContain(element);
        }

        // And every card on the rail can actually land somewhere in the object it
        // is shown for - which is the whole claim.
        const state = await (await page.request.get(`/api/documents/${FRONTMATTER}`)).json();
        const accepted = new Set();
        const walk = b => {
            for (const key of ['insertSiblings', 'insertChildren'])
                for (const o of b[key] || []) accepted.add(o.element);
            for (const c of b.blocks || []) walk(c);
        };
        for (const section of state.model.sections) section.blocks.forEach(walk);
        for (const element of inFrontMatter) expect([...accepted]).toContain(element);
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

        // A step belongs in the procedure, not in the common information. It is on
        // the rail, because it can go elsewhere in this module - so this is the
        // case that matters: a component the author may legitimately be holding,
        // over a block that will not take it. Dropping it must do nothing at all -
        // not insert it, not mark the document dirty, not put a step on the undo
        // stack.
        const target = '/dmodule[1]/content[1]/procedure[1]/commonInfo[1]/para[1]';
        const before = await editor.xml();
        await editor.drag('Step', target);

        expect(await editor.xml()).toBe(before);
        expect((await editor.state()).undo.depth).toBe(0);

        // Refused, but not in silence: the block the pointer is over says so, or
        // the author cannot tell "not here" from "this editor is broken".
        await editor.dragOver('Step', target);
        await expect(page.locator(`[data-path="${target}"]`)).toHaveClass(/s1kd-drop-refused/);
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
