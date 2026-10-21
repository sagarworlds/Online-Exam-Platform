import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi } from './real-api';

// What happens when an author presses Save on the question bank. A question is written in a panel down the side of the page. Its form is
// long, so whatever the page says about Save has to be where the author is looking: the panel keeps it in a footer that stays in view
// (FR-9). That covers the warning that the bank already has the question, the confirmation that it was saved, and the reason the bank
// refused it. The real API decides what a repeat is and what it refuses, so the page and the API are tested together; nothing is stubbed.

const run = randomUUID().slice(0, 8);

let api: RealApi;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
});

test.afterAll(async () => {
  await api.dispose();
});

const panel = (page: Page): Locator => page.getByRole('dialog', { name: 'New question' });

/** Opens the question bank signed in as the administrator, and opens the panel for a new question, which is closed until it is asked for. */
async function openQuestionBank(page: Page): Promise<void> {
  await api.openAdminPage(page, '/admin/questions');
  await page.getByRole('button', { name: 'New question' }).click();
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
  // Exact: "Remove accepted answer 1" is a name that contains this one.
  await page.getByLabel('Accepted answer 1', { exact: true }).fill('Paris');
}

// "Save and add another" leaves the panel open and empty, which is what lets these tests save the same question twice.
const save = (page: Page): Locator => page.getByRole('button', { name: 'Save and add another' });
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

  test('shows all of the warning on a short screen, where the footer is smaller than the warning and scrolls', async ({ page }) => {
    await page.setViewportSize({ width: 800, height: 420 });
    const text = `Which colour is a lemon? (${run})`;
    await openQuestionBank(page);
    await fillChoiceQuestion(page, text);
    await saveFirstTime(page);
    await fillChoiceQuestion(page, text);

    await save(page).click();

    // The footer is at most 45% of a screen this short, and the warning with its buttons needs more, so the footer scrolls. Engines differ in
    // how far focus moves a message that is only partly in view (WebKit left it flush against the edge, a pixel short), so the panel does it.
    await expect(warning(page)).toBeInViewport({ ratio: 1 });
    await expect(warning(page)).toBeFocused();
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

// The list is a line per question, so the page stays short however many there are: a question opens to its options, its actions are icons
// with a name and a tooltip, and the filters that are on show as chips that take them off again.
const row = (page: Page, text: string): Locator => page.locator('app-question-row').filter({ hasText: text });

/** Saves a choice question through the panel and closes it, which it does at once as nothing is left in it, and waits for its line to be in the list. */
async function addQuestion(page: Page, text: string): Promise<void> {
  await fillChoiceQuestion(page, text);
  await saveFirstTime(page);
  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(panel(page)).toHaveCount(0);
  await expect(row(page, text)).toHaveCount(1);
}

test.describe('the list of questions', () => {
  test('shows a saved question as one line, which opens to its options and closes again', async ({ page }) => {
    const text = `Which colour is a leaf? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);
    const line = row(page, text);

    await expect(line.getByText('Green')).toHaveCount(0);
    await line.getByRole('button', { name: 'Show details' }).click();
    await expect(line.getByText('Green')).toBeVisible();
    await expect(line.getByText('correct', { exact: true })).toBeVisible();

    await line.getByRole('button', { name: 'Hide details' }).click();
    await expect(line.getByText('Green')).toHaveCount(0);
  });

  test('names every icon and shows its tooltip when the icon is focused, and Escape puts it away', async ({ page }) => {
    const text = `Which colour is a banana? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);
    const line = row(page, text);
    // Adding the question left the pointer where the Save button was, which is over one of the row's icons once the list has been redrawn;
    // a resting pointer shows a hover tip of its own, and two tips on screen are not what is being checked. The pointer is moved away first.
    await page.mouse.move(0, 0);

    for (const name of ['Edit', 'Translations', 'Statistics', 'File under…', 'Delete', 'Show details']) {
      const icon = line.getByRole(name === 'Edit' ? 'link' : 'button', { name, exact: true });
      await icon.focus();
      await expect(page.getByRole('tooltip')).toHaveText(name);
    }

    await page.keyboard.press('Escape');
    await expect(page.getByRole('tooltip')).toHaveCount(0);
  });

  test('shows a filter that is on as a chip, narrows the list by it, and brings the list back when the chip is removed', async ({ page }) => {
    const text = `Which colour is snow? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);

    await page.getByLabel('Search questions').fill(text);
    await page.getByLabel('Search questions').press('Enter');
    await expect(row(page, text)).toHaveCount(1);

    await page.getByRole('button', { name: 'Filters', exact: true }).click();
    await page.locator('#filter-language').selectOption('hi');
    await expect(row(page, text)).toHaveCount(0);
    await expect(page.locator('.filter-chip')).toContainText('Language: Hindi');

    await page.getByRole('button', { name: 'Remove filter: Language: Hindi' }).click();
    await expect(page.locator('.filter-chip')).toHaveCount(0);
    await expect(row(page, text)).toHaveCount(1);
  });

  test('opens import and export in a dialog and closes it with Escape', async ({ page }) => {
    await api.openAdminPage(page, '/admin/questions');

    await page.getByRole('button', { name: 'Import and export' }).click();
    const dialog = page.getByRole('dialog', { name: 'Import and export' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Download questions' })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
  });

  test('fits a phone: no sideways scroll, and each line and its icons are within the screen and targets of at least 44px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    const text = `Which colour is the sky? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);
    const line = row(page, text);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);

    // 43.5: Firefox reports a 44px control as 43.99997.
    for (const name of ['Translations', 'Statistics', 'Delete', 'Show details']) {
      const box = await line.getByRole('button', { name, exact: true }).boundingBox();
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(43.5);
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(43.5);
      expect((box?.x ?? -1) + (box?.width ?? 0)).toBeLessThanOrEqual(360);
    }
  });
});

// A question is added or edited in a panel down the side of the page, so the list, its filters and how far it has been read stay behind it.
test.describe('the panel for a question', () => {
  test('opens over the list as a sheet, saves and closes, and the new question is in the list', async ({ page }) => {
    const text = `Which colour is a plum? (${run})`;
    await openQuestionBank(page);
    await expect(panel(page)).toBeVisible();
    await fillChoiceQuestion(page, text);

    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(panel(page)).toHaveCount(0);
    await expect(page.getByText('Question saved.')).toBeVisible();
    await expect(row(page, text)).toHaveCount(1);
  });

  test('Save and add another keeps the panel open with an empty form, the cursor in it, and the saved question in the list behind', async ({ page }) => {
    const text = `Which colour is a lime? (${run})`;
    await openQuestionBank(page);
    await fillChoiceQuestion(page, text);

    await save(page).click();

    await expect(page.getByText('Question saved.')).toBeVisible();
    await expect(panel(page)).toBeVisible();
    await expect(page.getByLabel('Option 1 text')).toHaveValue('');
    await expect(page.locator('#question-text')).toBeFocused();
    await expect(row(page, text)).toHaveCount(1);
  });

  test('asks before it throws away what was typed, and Keep editing goes on with it', async ({ page }) => {
    await openQuestionBank(page);
    await typeQuestion(page, `Half typed (${run})`);

    // Not from the text itself: the editor takes Escape for its own use, so there it leaves the panel as it is.
    await page.getByLabel('Option 1 text').focus();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('alertdialog')).toContainText('Throw away what you have typed?');
    await expect(panel(page)).toBeVisible();

    await page.getByRole('button', { name: 'Keep editing' }).click();
    await expect(page.locator('#question-text')).toContainText('Half typed');

    await page.getByRole('button', { name: 'Cancel' }).click();
    await page.getByRole('button', { name: 'Discard' }).click();
    await expect(panel(page)).toHaveCount(0);
  });

  test('edits a question from its Edit icon without leaving the page: the address changes, the list stays, and Save changes closes it', async ({ page }) => {
    const text = `Which colour is a cherry? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);
    // Something typed on the page itself: it is still there afterwards only if the page was not rebuilt.
    await page.getByLabel('Search questions').fill(text);
    await page.getByLabel('Search questions').press('Enter');
    await expect(row(page, text)).toHaveCount(1);

    await row(page, text).getByRole('link', { name: 'Edit', exact: true }).click();

    await expect(page).toHaveURL(/\/admin\/questions\/[0-9a-f-]+\/edit$/);
    const dialog = page.getByRole('dialog', { name: 'Edit question' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Option 1 text')).toHaveValue('Green');

    await dialog.getByLabel('Option 1 text').fill('Dark green');
    await dialog.getByRole('button', { name: 'Save changes' }).click();

    await expect(dialog).toHaveCount(0);
    await expect(page).toHaveURL(/\/admin\/questions$/);
    await expect(page.getByLabel('Search questions')).toHaveValue(text);
    await row(page, text).getByRole('button', { name: 'Show details' }).click();
    await expect(row(page, text).getByText('Dark green')).toBeVisible();
  });

  test('opens straight at the address of an edit, and closing it leaves the list', async ({ page }) => {
    const text = `Which colour is a pear? (${run})`;
    await openQuestionBank(page);
    await addQuestion(page, text);
    const href = await row(page, text).getByRole('link', { name: 'Edit', exact: true }).getAttribute('href');

    await api.openAdminPage(page, href as string);

    const dialog = page.getByRole('dialog', { name: 'Edit question' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Option 1 text')).toHaveValue('Green');

    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page).toHaveURL(/\/admin\/questions$/);
    await expect(row(page, text)).toHaveCount(1);
  });

  test('shows the tooltip of an icon inside the panel, which a modal dialog would otherwise cover', async ({ page }) => {
    await openQuestionBank(page);

    await page.getByRole('button', { name: 'Keep option 1 in place when options are shuffled' }).focus();

    const tip = page.getByRole('tooltip');
    await expect(tip).toHaveText('Keep in place when options are shuffled');
    await expect(tip).toBeInViewport({ ratio: 1 });
    // The bubble is inside the dialog, in the browser's top layer, not behind it in the page.
    await expect(page.locator('dialog .app-tooltip')).toHaveCount(1);
  });

  test('keeps an option in place with its pin, which says whether it is pressed', async ({ page }) => {
    await openQuestionBank(page);
    const pin = page.getByRole('button', { name: 'Keep option 2 in place when options are shuffled' });
    await expect(pin).toHaveAttribute('aria-pressed', 'false');

    await pin.click();

    await expect(pin).toHaveAttribute('aria-pressed', 'true');
  });

  test('folds difficulty, topics and the explanation behind More details', async ({ page }) => {
    await openQuestionBank(page);
    const more = page.getByRole('button', { name: 'More details' });
    // By id: the list's own filters have a Difficulty and a Topic select too, folded away behind the panel.
    const difficulty = page.locator('#question-difficulty');
    await expect(more).toHaveAttribute('aria-expanded', 'false');
    await expect(difficulty).toBeHidden();

    await more.click();

    await expect(more).toHaveAttribute('aria-expanded', 'true');
    await expect(difficulty).toBeVisible();
    await expect(page.locator('#question-topics')).toBeVisible();
    await expect(page.locator('#question-explanation')).toBeVisible();
  });

  test('fills a phone: as wide as the screen, with no sideways scroll, and its buttons and icons are targets of at least 44px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await openQuestionBank(page);

    const sheet = await page.locator('dialog.modal--side').boundingBox();
    expect(sheet?.width ?? 0).toBeGreaterThanOrEqual(359);
    expect(sheet?.width ?? 0).toBeLessThanOrEqual(360);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);

    // 43.5: Firefox reports a 44px control as 43.99997.
    for (const name of ['Save', 'Save and add another', 'Cancel', 'Keep option 1 in place when options are shuffled', 'Remove option 1']) {
      const box = await page.getByRole('button', { name, exact: true }).boundingBox();
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(43.5);
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(43.5);
      expect((box?.x ?? -1) + (box?.width ?? 0)).toBeLessThanOrEqual(360);
    }
  });
});
