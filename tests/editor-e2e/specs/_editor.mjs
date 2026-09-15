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
export const PROCEDURE = 'DMC-AE100-A-27-81-00-00A-720A-A_002-00_EN-GB';

/** A descriptive module: levelled paragraphs with titles. */
export const DESCRIPTIVE = 'DMC-AE100-A-27-81-00-00A-042A-A_002-00_EN-GB';

/** Front matter: a list of effective data modules, whose shape the schema fixes. */
export const FRONTMATTER = 'DMC-AE100-A-00-00-0000-00A-002A-D_001-00_EN-GB';

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
        await this.reach(path);
        await this.block(path).locator(`> .s1kd-gutter > .s1kd-gutter-${action}`).click();
        await this.page.waitForTimeout(400);
    }

    /** Open the block's insert menu and choose an element by the name it is offered under. */
    async insert(path, label) {
        await this.reach(path);
        await this.block(path).locator('> .s1kd-gutter > .s1kd-gutter-insert').click();
        await this.page.locator('.tss-contextmenu-item').filter({ hasText: label }).first().click();
        await this.page.waitForTimeout(500);
    }

    /**
     * Hover a block so that *its* gutter is the one showing.
     *
     * Its top-left corner, not its centre. Only the innermost block under the
     * pointer offers its commands — otherwise a step inside a step inside a
     * procedure stacks three columns of buttons across the margin — so hovering a
     * container in the middle, where its children are, withdraws the very gutter
     * the caller is reaching for.
     */
    async reach(path) {
        const block = this.block(path);
        await block.scrollIntoViewIfNeeded();

        // Move the pointer away first. Every command redraws the page, and Chromium
        // does not recompute :hover for a freshly inserted element while the pointer
        // is stationary — so hovering the same spot twice in a row is a no-op move
        // and the gutter stays hidden. A person moves the mouse between commands;
        // a test has to be told to.
        await this.page.mouse.move(0, 0);
        await block.hover({ position: { x: 4, y: 4 } });
    }

    /**
     * Travel to a gutter button the way a hand does: in small steps, through
     * whatever lies between the text and the buttons.
     *
     * `locator.click()` teleports the pointer onto its target in one move and so
     * never samples the path. That is the difference between a gutter a person can
     * press and one only a test can: the gutter hides when the block stops being
     * hovered, and a hidden gutter is `pointer-events: none`, so a gap anywhere
     * along the way hides the buttons before the pointer arrives and they cannot
     * be hit even head-on.
     *
     * Returns the CSS opacity sampled at each step, so a test can assert the
     * buttons stayed up for the whole journey rather than only at its end.
     */
    async walkToGutter(path, action) {
        const block = this.block(path);
        await block.scrollIntoViewIfNeeded();
        await this.page.mouse.move(0, 0);

        // Start on the block's *own* area. Only the innermost block under the
        // pointer offers its commands, so starting anywhere a child covers would
        // watch the wrong gutter and call a correct editor broken. A block with
        // text of its own is entered through it; a container whose content is all
        // children is entered through the label strip at its top-left, which is
        // the only part of it a pointer can be on and have it be innermost.
        const own = block.locator('> .s1kd-body > .s1kd-text');
        const from = (await own.count())
            ? await own.boundingBox().then(b => ({ x: b.x + Math.min(120, b.width / 2),
                                                   y: b.y + b.height / 2 }))
            : await block.boundingBox().then(b => ({ x: b.x + 6, y: b.y + 6 }));

        await this.page.mouse.move(from.x, from.y);
        await this.page.waitForTimeout(150);

        const gutter = block.locator(`> .s1kd-gutter`);
        const target = block.locator(`> .s1kd-gutter > .s1kd-gutter-${action}`);
        const tbox = await target.boundingBox();
        const to = { x: tbox.x + tbox.width / 2, y: tbox.y + tbox.height / 2 };

        const seen = [];
        for (let i = 1; i <= 12; i++) {
            await this.page.mouse.move(from.x + (to.x - from.x) * i / 12,
                                      from.y + (to.y - from.y) * i / 12);
            await this.page.waitForTimeout(25);
            seen.push(Number(await gutter.evaluate(g => getComputedStyle(g).opacity)));
        }

        return { opacities: seen, target };
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

    /**
     * Drag a component over a block and leave it hovering there, without dropping.
     *
     * `dragTo` completes the gesture, so it cannot be used to look at what the page
     * says *during* one. This drives the pointer itself and stops over the target,
     * which is also the only way to see the answer a refused drop gives.
     */
    async dragOver(label, path) {
        const target = this.block(path);

        // Centred, not merely scrolled into view. A drag whose pointer approaches
        // the edge of the scroller makes it auto-scroll, which moves the target out
        // from under the pointer mid-gesture and lands it on whatever slid into its
        // place.
        await target.evaluate(e => e.scrollIntoView({ block: 'center' }));
        await this.page.waitForTimeout(250);

        const card = this.card(label);
        const from = await card.boundingBox();
        const to = await target.boundingBox();
        const x = to.x + to.width / 2;
        const y = to.y + to.height / 2;

        await this.page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
        await this.page.mouse.down();

        // In many small steps. The browser only raises drag events for a pointer it
        // believes is dragging, and a handful of long jumps produces a handful of
        // them - too few to be sure the one over the target ever happened.
        for (let i = 1; i <= 24; i++) {
            await this.page.mouse.move(from.x + (x - from.x) * i / 24,
                                       from.y + (y - from.y) * i / 24, { steps: 2 });
            await this.page.waitForTimeout(20);
        }
        await this.page.waitForTimeout(120);
    }

    /** Let go of whatever `dragOver` is holding, without dropping it on anything. */
    async abandonDrag() {
        await this.page.keyboard.press('Escape');
        await this.page.mouse.up();
        await this.page.waitForTimeout(150);
    }

    /** The whole editor state, for assertions about history and dirtiness. */
    async state() {
        const response = await this.page.request.get(`/api/documents/${this.id}`);
        return response.json();
    }
}
