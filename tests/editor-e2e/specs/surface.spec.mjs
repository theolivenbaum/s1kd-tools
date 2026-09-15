import { test, expect } from '@playwright/test';
import { openEditor, PROCEDURE, DESCRIPTIVE, REFERENCE_PARAGRAPH_PATH } from './_editor.mjs';

/**
 * What the surface draws, before anyone edits anything.
 *
 * These are assertions about the *projection* — that a data module arrives in the
 * browser as the page it will be, with its steps numbered and its warnings boxed —
 * rather than about editing. They are the ones that fail when the editing
 * stylesheet changes, which is exactly what they are for.
 */
test.describe('the editing surface', () => {
    test('lists the CSDB and opens a data module', async ({ page }) => {
        await page.goto('/');
        await page.locator('.s1kd-block').first().waitFor();

        const documents = await (await page.request.get('/api/documents')).json();
        expect(documents.length).toBe(10);

        // One sidebar entry per object in the CSDB, listed by what kind of object
        // it is — which is how someone trying the editor chooses one.
        for (const document of documents) {
            await expect(page.locator(`[id="${document.id}"]`)).toHaveText(document.objectType);
        }
    });

    test('draws a procedure as its page', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        await expect(page.locator('.s1kd-page-title'))
            .toHaveText('Slat actuation power control unit — Installation');
        await expect(page.locator('.s1kd-page-type')).toHaveText('Procedure');

        // The ATA numbering the printed page uses: 1. for a top-level step, A. for
        // one inside it. If these come out as 1/2/3 the projection has lost the
        // depth, and the editor is showing a different document from the PDF.
        const steps = page.locator('.s1kd-kind-step > .s1kd-label');
        await expect(steps.first()).toHaveText('1.');
        await expect(steps.nth(1)).toHaveText('A.');

        await expect(page.locator('.s1kd-kind-warning')).toHaveCount(2);
        await expect(page.locator('.s1kd-kind-caution')).toHaveCount(1);
        await expect(page.locator('.s1kd-kind-warning').first()).toContainText('WARNING');
        await expect(page.locator('.s1kd-kind-figure')).toHaveCount(1);
    });

    test('offers the address as labelled fields', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        const ident = page.locator('.s1kd-section-ident');
        await expect(ident.locator('.s1kd-block').filter({ hasText: 'Technical name' }))
            .toContainText('Slat actuation power control unit');

        // The issue number is an attribute, so it is an input rather than a
        // contenteditable — a value with a shape, not prose.
        const issue = ident.locator('.s1kd-block').filter({ hasText: 'Issue number' });
        await expect(issue.locator('input')).toHaveValue('002');

        // A referenced module's own dmTitle must not be offered as this one's name.
        await expect(ident.locator('.s1kd-block').filter({ hasText: 'Technical name' }))
            .toHaveCount(1);
    });

    test('an address field is a block with a box, not just two grid cells', async ({ page }) => {
        await openEditor(page, PROCEDURE);

        const fields = page.locator('.s1kd-section-ident > .s1kd-section-body > .s1kd-block');
        const count = await fields.count();
        expect(count).toBeGreaterThan(3);

        // The section lays its labels and values out as two columns, and a field
        // used to become `display: contents` to take part in them. That removes the
        // element from layout altogether: it has no box, so `position: relative`
        // has nothing to apply to and the field's own gutter anchors to the page
        // instead - every one of them stacked off the top-left corner at (-34, 0).
        // The same went for the body, which is what the reveal and the drop line
        // are painted on, so a check finding could land on a field and show nothing.
        const labelX = new Set();
        const valueX = new Set();

        for (let i = 0; i < count; i++) {
            const field = fields.nth(i);
            const box = await field.boundingBox();
            expect(box.width).toBeGreaterThan(0);
            expect(box.height).toBeGreaterThan(0);

            // Its gutter belongs to its own row, in the margin beside it.
            const gutter = await field.locator('> .s1kd-gutter').boundingBox();
            expect(gutter.x).toBeLessThan(box.x);
            expect(Math.abs(gutter.y - box.y)).toBeLessThan(2);

            // And its body can paint, which is what a reveal needs.
            const body = await field.locator('> .s1kd-body').boundingBox();
            expect(body.width).toBeGreaterThan(0);
            expect(body.height).toBeGreaterThan(0);

            labelX.add(Math.round((await field.locator('> .s1kd-label').boundingBox()).x));
            valueX.add(Math.round(body.x));
        }

        // All of which had to happen without losing the two columns: every label
        // starts on one edge and every value on another.
        expect([...labelX]).toHaveLength(1);
        expect([...valueX]).toHaveLength(1);
    });

    test('shows an inline reference as a chip rather than as text', async ({ page }) => {
        const editor = await openEditor(page, PROCEDURE);

        const chip = editor.text(REFERENCE_PARAGRAPH_PATH).locator('.s1kd-chip');
        await expect(chip).toHaveText('Slat actuation power control unit — Removal');

        // Not editable, and carrying what it points at — the two things that make it
        // a reference rather than a phrase that happens to look like one.
        await expect(chip).toHaveAttribute('contenteditable', 'false');
        await expect(chip).toHaveAttribute('title', 'DMC-AE100-A-27-81-00-00A-520A-A');
    });

    test('draws a descriptive module as levelled sections', async ({ page }) => {
        await openEditor(page, DESCRIPTIVE);

        await expect(page.locator('.s1kd-page-type')).toHaveText('Descriptive');

        // Levelled paragraphs are numbered by depth, as they are on the page.
        const sections = page.locator('.s1kd-kind-section > .s1kd-label');
        await expect(sections.first()).toHaveText('1');
        await expect(page.locator('.s1kd-kind-title').first()).toBeVisible();
    });
});
