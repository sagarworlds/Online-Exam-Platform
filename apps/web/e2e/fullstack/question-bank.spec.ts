import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi } from './real-api';

// An author adds a question the bank already has. The bank refuses it and the page warns the author, so the warning has to be where the
// author is looking: the form is long, and when this was above the form, pressing Save appeared to do nothing (FR-9). The real API decides
// what a repeat is, so the page and the API are tested together; nothing is stubbed.

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

/** Saves the question once, so the bank has it. */
async function saveFirstTime(page: Page): Promise<void> {
  await save(page).click();
  await expect(page.getByText('Question saved.')).toBeVisible();
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
    await expect(page.getByText('Question saved.')).toBeVisible();
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
});
