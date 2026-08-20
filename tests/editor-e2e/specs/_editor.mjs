import { expect } from '@playwright/test';

/**
 * The data modules the tests use, and the handful of gestures they all need.
 *
 * The editor's state lives on the server — one session per data module, shared —
 * so a test that edits one changes what the next test sees. `openEditor` reverts
 * the module before opening it, which is what keeps the suite order-independent
 * without giving each test its own server.
 */

/** A procedure: numbered steps, warnings, a figure, an inline reference. */
export const PROCEDURE = 'DMC-A350X-A-27-81-00-00A-720A-A_002-00_EN-GB';

/** A descriptive module: levelled paragraphs with titles. */
export const DESCRIPTIVE = 'DMC-A350X-A-27-81-00-00A-042A-A_002-00_EN-GB';

/** The paragraph that carries the inline dmRef, in the module as it ships. */
export const REFERENCE_PARAGRAPH_PATH =
    '/dmodule[1]/content[1]/procedure[1]/commonInfo[1]/para[1]';

/**
 * Open a data module in a browser that has just been told to forget it.
 *
 * The revert goes through the API rather than the Revert button, because the
 * button asks for confirmation — correctly, since it discards work — and a test
 * that dismissed a dialog to reach its starting state would be testing the dialog.
 */
export async function openEditor(page, id = PROCEDURE) {
    await page.request.post(`/api/documents/${id}/revert`);

    await page.goto('/');
    await page.locator('.s1kd-block').first().waitFor();
    await page.locator(`[id="${id}"]`).click();

    // The code is the thing that only changes when a different module is open, so
    // it is what says the click landed rather than a block count that two modules
    // could share.
    await expect(page.locator('.s1kd-page-code')).toHaveText(codeOf(id));

    return new Editor(page, id);
}

/** The data module code inside a CSDB file name. */
export function codeOf(id) {
    return id.replace(/_\d{3}-\d{2}_[A-Z]{2}-[A-Z]{2}$/, '');
}

/** The gestures a test makes, named as the author's actions rather than as clicks. */
export class Editor {
    constructor(page, id) {
        this.page = page;
        this.id = id;
    }

    /** The block whose path is exactly this. */
    block(path) {
        return this.page.locator(`[data-path="${path}"]`);
    }

    /** The editable text of the block at a path. */
    text(path) {
        return this.block(path).locator('> .s1kd-body > .s1kd-text');
    }

    /** The first block of a kind whose text contains a phrase. */
    blockContaining(phrase, kind = null) {
        const selector = kind ? `.s1kd-block.s1kd-kind-${kind}` : '.s1kd-block';
        return this.page.locator(selector).filter({ hasText: phrase }).last();
    }

    /**
     * Type into a block and leave it, which is what commits the edit.
     *
     * The blur is the gesture, not an implementation detail leaking into the test:
     * the editor commits when the author leaves a block, so a test that saved
     * without leaving would be asserting something the editor does not promise.
     */
    async retype(path, value) {
        const text = this.text(path);
        await text.click();
        await text.selectText();
        await this.page.keyboard.type(value);
        await this.commit();
    }

    /** Leave whatever block is being edited, and wait for the redraw. */
    async commit() {
        await this.page.locator('.s1kd-page-title').click();
        await this.page.waitForTimeout(400);
    }

    /**
     * Press a per-block gutter command: up, down or delete.
     *
     * Scoped with `>` to the block's own gutter. A block's descendants have gutters
     * too, and they come first in document order, so an unscoped lookup presses a
     * nested block's button and quietly tests the wrong thing.
     */
    async gutter(path, action) {
        await this.block(path).hover();
        await this.block(path).locator(`> .s1kd-gutter > .s1kd-gutter-${action}`).click();
        await this.page.waitForTimeout(400);
    }

    /** Open the block's insert menu and choose an element by the name it is offered under. */
    async insert(path, label) {
        await this.block(path).hover();
        await this.block(path).locator('> .s1kd-gutter > .s1kd-gutter-insert').click();
        await this.page.locator('.tss-contextmenu-item').filter({ hasText: label }).first().click();
        await this.page.waitForTimeout(500);
    }

    /**
     * A command-bar button, by its accessible name.
     *
     * A substring rather than a regular expression: Playwright matches a regex
     * against the raw accessible name, which for these carries the whitespace
     * around the icon, so /^Undo/ finds nothing while "Undo" finds it.
     */
    command(name) {
        return this.page.locator('.s1kd-commandbar')
            .getByRole('button', { name, exact: false }).first();
    }

    /** Switch to one of the three views, and let it settle. */
    async show(tab) {
        await this.page.getByRole('tab', { name: tab }).click();
        await this.page.waitForTimeout(600);
    }

    /** The document as the server holds it, read through the API rather than the UI. */
    async xml() {
        const response = await this.page.request.get(`/api/documents/${this.id}`);
        return (await response.json()).xml;
    }

    /** A palette card, by the name it is offered under. */
    card(label) {
        return this.page.locator('.s1kd-palette-card').filter({ hasText: label }).first();
    }

    /**
     * Drag a component out of the palette onto a block.
     *
     * `dragTo` rather than mouse down/move/up: HTML5 drag and drop is not driven by
     * synthetic mouse events, so the hand-rolled version presses the card and
     * releases it over the target without a single drag event having fired — and
     * passes or fails for reasons that have nothing to do with the editor.
     */
    async drag(label, path, edge = 'top') {
        const target = this.block(path);
        await target.scrollIntoViewIfNeeded();

        const box = await target.boundingBox();
        await this.card(label).dragTo(target, {
            targetPosition: { x: box.width / 2, y: edge === 'top' ? 3 : box.height - 3 },
        });
        await this.page.waitForTimeout(600);
    }

    /** The whole editor state, for assertions about history and dirtiness. */
    async state() {
        const response = await this.page.request.get(`/api/documents/${this.id}`);
        return response.json();
    }
}
