import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi, type DraftExam, type QuestionSpec } from './real-api';

// Taking a question or a section out of a draft exam: a trash icon, then a question the author answers, and only then does the real API
// remove anything. The API decides what is left afterwards, so the page and the API are tested together; nothing is stubbed.

let api: RealApi;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
});

test.afterAll(async () => {
  await api.dispose();
});

/**
 * A draft exam with two questions in its one section, opened in the editor as the administrator. Every draft has questions of its own
 * wording, since the bank refuses a second question that repeats one it has.
 */
async function openDraft(page: Page): Promise<{ exam: DraftExam; first: QuestionSpec; second: QuestionSpec }> {
  const id = randomUUID().slice(0, 8);
  const first = { text: `Which colour is grass? (${id}-a)`, right: 'Green', wrong: 'Red' };
  const second = { text: `Which colour is the sea? (${id}-b)`, right: 'Blue', wrong: 'Red' };
  const exam = await api.draftExam(`Remove ${id}`, [first, second]);
  await api.openAdminPage(page, `/exams/${exam.id}`);
  await expect(page.getByRole('heading', { name: `1. ${exam.sectionName}` })).toBeVisible();
  return { exam, first, second };
}

const questionRows = (page: Page): Locator => page.locator('app-exam-section-card li');
const trashFor = (page: Page, text: string): Locator => page.getByRole('button', { name: `Remove ${text} from the exam` });

test.describe('removing from a draft exam', () => {
  test('a question goes only after the author confirms, and Cancel keeps it', async ({ page }) => {
    const { first, second } = await openDraft(page);
    await expect(questionRows(page)).toHaveCount(2);
    // No "Remove" words on the page yet: the controls are icons.
    await expect(page.getByRole('button', { name: 'Remove', exact: true })).toHaveCount(0);

    await trashFor(page, first.text).click();
    const asked = page.getByRole('alertdialog');
    await expect(asked).toContainText('Remove this question from the exam? It stays in the question bank.');
    await expect(asked.getByRole('button', { name: 'Cancel' })).toBeFocused();
    await expect(questionRows(page)).toHaveCount(2);

    await asked.getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByRole('alertdialog')).toHaveCount(0);
    await expect(trashFor(page, first.text)).toBeFocused();
    await expect(questionRows(page)).toHaveCount(2);

    await trashFor(page, first.text).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Remove', exact: true }).click();
    await expect(questionRows(page)).toHaveCount(1);
    // The list holds the other question only; the removed one is back in the picker, ready to be added again.
    await expect(questionRows(page)).toContainText(second.text);
    await expect(questionRows(page)).not.toContainText(first.text);
  });

  test('Escape takes the question back and nothing is removed', async ({ page }) => {
    const { second } = await openDraft(page);

    await trashFor(page, second.text).click();
    await expect(page.getByRole('alertdialog')).toBeVisible();
    await page.keyboard.press('Escape');

    await expect(page.getByRole('alertdialog')).toHaveCount(0);
    await expect(questionRows(page)).toHaveCount(2);
  });

  test('a section goes with its questions only after the author confirms', async ({ page }) => {
    const { exam } = await openDraft(page);

    await page.getByRole('button', { name: `Remove section ${exam.sectionName}` }).click();
    const asked = page.getByRole('alertdialog');
    await expect(asked).toContainText('Remove this section and its 2 questions from the exam?');
    await expect(page.getByRole('heading', { name: `1. ${exam.sectionName}` })).toBeVisible();

    await asked.getByRole('button', { name: 'Remove section' }).click();
    await expect(page.getByText('No sections yet.')).toBeVisible();
  });

  test('fits a phone: the icons are at least 44px, and the question wraps without the page scrolling sideways', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    const { first } = await openDraft(page);

    const icon = trashFor(page, first.text);
    const box = await icon.boundingBox();
    expect(box?.width ?? 0).toBeGreaterThanOrEqual(44);
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);

    await icon.click();
    const asked = page.getByRole('alertdialog');
    await expect(asked).toBeInViewport({ ratio: 1 });
    for (const name of ['Remove', 'Cancel']) {
      const target = await asked.getByRole('button', { name, exact: true }).boundingBox();
      expect(target?.height ?? 0).toBeGreaterThanOrEqual(44);
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
