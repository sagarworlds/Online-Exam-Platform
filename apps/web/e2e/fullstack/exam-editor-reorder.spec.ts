import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi, type DraftExam, type QuestionSpec } from './real-api';

// Putting a draft exam's questions and sections in another order: arrows on the page, and the real API keeps the order. Nothing is stubbed, so
// the page and the API are tested together, including what is still there after the page is loaded afresh.

let api: RealApi;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
});

test.afterAll(async () => {
  await api.dispose();
});

/** A draft exam with two questions in its one section, opened in the editor as the administrator. */
async function openDraft(page: Page): Promise<{ exam: DraftExam; first: QuestionSpec; second: QuestionSpec }> {
  const id = randomUUID().slice(0, 8);
  const first = { text: `Which colour is grass? (${id}-a)`, right: 'Green', wrong: 'Red' };
  const second = { text: `Which colour is the sea? (${id}-b)`, right: 'Blue', wrong: 'Red' };
  const exam = await api.draftExam(`Reorder ${id}`, [first, second]);
  await api.openAdminPage(page, `/exams/${exam.id}`);
  await expect(page.getByRole('heading', { name: `1. ${exam.sectionName}` })).toBeVisible();
  return { exam, first, second };
}

const questionRows = (page: Page): Locator => page.locator('app-exam-section-card li');
const arrow = (page: Page, text: string, direction: 'up' | 'down'): Locator => page.getByRole('button', { name: `Move ${text} ${direction}`, exact: true });

test.describe('putting a draft exam in another order', () => {
  test('a question moves down, focus stays on the pair of arrows, and the order is still there after a reload', async ({ page }) => {
    const { first, second } = await openDraft(page);
    await expect(questionRows(page).nth(0)).toContainText(first.text);

    await arrow(page, first.text, 'down').click();

    await expect(questionRows(page).nth(0)).toContainText(second.text);
    await expect(questionRows(page).nth(1)).toContainText(first.text);
    // The question is last now, so its down arrow has nowhere to go: focus is on the arrow that still does.
    await expect(arrow(page, first.text, 'down')).toHaveAttribute('aria-disabled', 'true');
    await expect(arrow(page, first.text, 'up')).toBeFocused();

    await page.reload();
    await expect(questionRows(page).nth(0)).toContainText(second.text);
    await expect(questionRows(page).nth(1)).toContainText(first.text);
  });

  test('the first question cannot go up and the last cannot go down, and the arrows say so', async ({ page }) => {
    const { first, second } = await openDraft(page);

    await expect(arrow(page, first.text, 'up')).toHaveAttribute('aria-disabled', 'true');
    await expect(arrow(page, second.text, 'down')).toHaveAttribute('aria-disabled', 'true');
    await expect(arrow(page, first.text, 'down')).not.toHaveAttribute('aria-disabled', 'true');
  });

  test('a section added later moves above the first, and the exam is read back in that order', async ({ page }) => {
    const { exam } = await openDraft(page);
    await page.getByLabel('New section name').fill('Second part');
    await page.getByRole('button', { name: 'Add section' }).click();
    await expect(page.getByRole('heading', { name: '2. Second part' })).toBeVisible();

    await page.getByRole('button', { name: 'Move up: Second part', exact: true }).click();

    await expect(page.getByRole('heading', { name: '1. Second part' })).toBeVisible();
    await expect(page.getByRole('heading', { name: `2. ${exam.sectionName}` })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('heading', { name: '1. Second part' })).toBeVisible();
    await expect(page.getByRole('heading', { name: `2. ${exam.sectionName}` })).toBeVisible();
  });
});
