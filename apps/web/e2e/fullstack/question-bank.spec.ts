import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi } from './real-api';

// What happens when an author presses Save on the question bank. The form is long, so whatever the page says about it has to be where the
// author is looking: when it was above the form, pressing Save appeared to do nothing (FR-9). That covers the warning that the bank already
// has the question, the confirmation that it was saved, and the reason the bank refused it. The real API decides what a repeat is and what
// it refuses, so the page and the API are tested together; nothing is stubbed.

const run = randomUUID().slice(0, 8);
const AUTH_TOKEN_STORAGE_KEY = 'exam-platform.auth-token';

let api: RealApi;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
});

test.afterAll(async () => {
  await api.dispose();
});

/** Opens the question bank signed in as the administrator, handing the page the session instead of signing in on it. */
async function openQuestionBank(page: Page): Promise<void> {
  const token = await api.administratorToken();
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), [AUTH_TOKEN_STORAGE_KEY, token] as [string, string]);
  await page.goto('/admin/questions');
  await expect(page.locator('#question-text')).toBeVisible();
}

async function typeQuestion(page: Page, text: string): Promise<void> {
  await page.locator('#question-text').click();
  await page.keyboard.type(text);
}

async function fillChoiceQuestion(page: Page, text: string): Promise<void> {
  await typeQuestion(page, text);
  await page.getByLabel('Option 1 text').fill('Green');
  await page.getByLabel('Option 2 text').fill('Red');
  await page.getByLabel('Option 1 is correct').check();
}

async function fillTextQuestion(page: Page, text: string): Promise<void> {
  await typeQuestion(page, text);
  await page.getByLabel('Question type').selectOption('text');
  await page.getByLabel('Accepted answer 1').fill('Paris');
}

const save = (page: Page): Locator => page.getByRole('button', { name: 'Save question' });
const warning = (page: Page): Locator => page.getByRole('alert').filter({ hasText: 'This question is already in the bank' });

/** Saves the question once, so the bank has it, and checks that the confirmation is in view beside the button. */
async function saveFirstTime(page: Page): Promise<void> {
  await save(page).click();
  await expect(page.getByText('Question saved.')).toBeInViewport({ ratio: 1 });
}

test.describe('adding a question the bank already has', () => {
  test('warns where the author is looking, with the question it repeats, and Add it anyway still saves it', async ({ page }) => {
    const text = `Which colour is grass? (${run})`;
    await openQuestionBank(page);
    await fillChoiceQuestion(page, text);
    await saveFirstTime(page);

    await fillChoiceQuestion(page, text);
    await save(page).click();

    // In view without scrolling, and focused: it is not somewhere above the form, out of sight.
    await expect(warning(page)).toBeInViewport({ ratio: 1 });
    await expect(warning(page)).toBeFocused();
    await expect(warning(page)).toContainText(text);
    await expect(warning(page)).toContainText('these options');

    await page.getByRole('button', { name: 'Add it anyway' }).click();
    await expect(page.getByText('Question saved.')).toBeInViewport({ ratio: 1 });
    await expect(warning(page)).toHaveCount(0);
  });

  test('Don\'t add it takes the warning away and keeps what the author typed', async ({ page }) => {
    const text = `Which colour is the sea? (${run})`;
    await openQuestionBank(page);
    await fillChoiceQuestion(page, text);
    await saveFirstTime(page);

    await fillChoiceQuestion(page, text);
    await save(page).click();
    await expect(warning(page)).toBeInViewport({ ratio: 1 });
    await page.getByRole('button', { name: "Don't add it" }).click();

    await expect(warning(page)).toHaveCount(0);
    await expect(page.getByLabel('Option 1 text')).toHaveValue('Green');
  });

  test('names the repeat of a text question too, by its accepted answers', async ({ page }) => {
    const text = `What is the capital of France? (${run})`;
    await openQuestionBank(page);
    await fillTextQuestion(page, text);
    await saveFirstTime(page);

    await fillTextQuestion(page, text);
    await save(page).click();

    await expect(warning(page)).toBeInViewport({ ratio: 1 });
    await expect(warning(page)).toContainText('these accepted answers');
    // Asked about the accepted answers, the bank finds the question it has just refused, so the warning is not an empty list.
    await expect(warning(page).locator('li')).toHaveCount(1);
    await expect(warning(page).locator('li')).toContainText(text);
  });

  test('shows why the bank refused a question where the author is looking, with focus on it', async ({ page }) => {
    await openQuestionBank(page);
    // The form lets any length through; the API refuses a question of more than 4,000 readable characters, which is a real refusal to show.
    await page.locator('#question-text').click();
    await page.keyboard.insertText('x'.repeat(4001));
    await page.getByLabel('Option 1 text').fill('Green');
    await page.getByLabel('Option 2 text').fill('Red');
    await page.getByLabel('Option 1 is correct').check();
    await save(page).click();

    const refusal = page.getByRole('alert').filter({ hasText: 'at most 4000 characters' });
    await expect(refusal).toBeInViewport({ ratio: 1 });
    await expect(refusal).toBeFocused();
    await expect(page.getByText('Question saved.')).toHaveCount(0);
  });
});
