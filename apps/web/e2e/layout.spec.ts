import type { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { choose, optionOf, signIn, startExam } from './support/journey';
import { text } from './support/messages';

// The pages a candidate uses, at the size and the way of holding of each device project (see playwright.shared.ts): a phone, a phone on
// its side, a small phone and a tablet. They check what a screen shows rather than what it looks like: nothing runs off the side
// (WCAG 1.4.10), the way forward is on screen, and the layout the width calls for is the one in use.

/** Whether the page scrolls sideways: something on it is wider than the window. */
const scrollsSideways = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);

/** The layouts are chosen by width (see exam.css): up to 40rem is a phone's. */
const isPhoneWidth = (page: Page) => (page.viewportSize()?.width ?? 0) <= 640;

test.describe('layout at the size of the screen', () => {
  test('the log-in page fits the width', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    expect(await scrollsSideways(page)).toBe(false);
  });

  test('the exams page fits the width, and its one action is within the screen', async ({ page, api }) => {
    await signIn(page, api);
    const start = page.getByRole('link', { name: text('myExams.start'), exact: true });
    await expect(start).toBeVisible();

    expect(await scrollsSideways(page)).toBe(false);
    const box = (await start.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
  });

  test('the header is one short row on a phone, with its links behind a Menu button, and all in view where there is room', async ({ page, api }) => {
    await signIn(page, api);
    const menu = page.getByRole('button', { name: text('nav.menu'), exact: false });
    const exams = page.getByRole('link', { name: text('nav.myExams'), exact: true });

    if (isPhoneWidth(page)) {
      await expect(menu).toBeVisible();
      await expect(exams).toBeHidden();
      // One short row: the page begins within the first 90 pixels instead of after three rows of links.
      const header = (await page.locator('header.nav').boundingBox())!;
      expect(header.height).toBeLessThan(90);

      await menu.click();
      await expect(menu).toHaveAttribute('aria-expanded', 'true');
      await expect(exams).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(exams).toBeHidden();
      await expect(menu).toBeFocused();
    } else {
      await expect(menu).toBeHidden();
      await expect(exams).toBeVisible();
    }
  });

  test('the exam screen fits the width, puts the site away, and leaves the way forward on screen', async ({ page, api }) => {
    await startExam(page, api);

    expect(await scrollsSideways(page)).toBe(false);
    // While the exam is open the header is the name and the language: no link out of the exam is one tap away.
    await expect(page.getByRole('link', { name: text('nav.myExams'), exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: text('nav.logout'), exact: true })).toHaveCount(0);
    await expect(page.getByRole('timer')).toBeVisible();

    const next = page.getByRole('button', { name: text('attempt.saveNext'), exact: false });
    if (isPhoneWidth(page)) {
      // Pinned to the bottom of the screen, so it is within the window without scrolling.
      await expect(next).toBeInViewport();
    } else {
      await next.scrollIntoViewIfNeeded();
      await expect(next).toBeInViewport();
    }
  });

  test('the palette is in a sheet on a phone and a pane beside the question where there is room', async ({ page, api }) => {
    await startExam(page, api);
    const pane = page.locator('.exam-side');
    const questions = page.locator('.exam-pager__palette');

    if (isPhoneWidth(page)) {
      await expect(pane).toBeHidden();
      await expect(questions).toBeVisible();
      await questions.click();

      const sheet = page.getByRole('dialog', { name: text('attempt.questions') });
      await expect(sheet).toBeVisible();
      await sheet.getByRole('button', { name: /^Question 2,/ }).click();

      await expect(sheet).toBeHidden();
      await expect(page.getByText(api.questions[1]!.text)).toBeVisible();
    } else {
      await expect(pane).toBeVisible();
      await expect(questions).toBeHidden();
      await pane.getByRole('button', { name: /^Question 2,/ }).click();

      await expect(page.getByText(api.questions[1]!.text)).toBeVisible();
    }
  });

  test('turning the device keeps the answer and the layout still fits', async ({ page, api }) => {
    await startExam(page, api);
    await choose(page, 'Answer 2');
    const { width, height } = page.viewportSize()!;

    await page.setViewportSize({ width: height, height: width });

    await expect(optionOf(page, 'Answer 2')).toBeChecked();
    expect(await scrollsSideways(page)).toBe(false);
    await expect(page.getByRole('timer')).toBeVisible();
  });
});
