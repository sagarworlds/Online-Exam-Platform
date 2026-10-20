import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi } from './real-api';

// The question bank says whether its questions are encrypted with a padlock beside the heading, and keeps the details in a tip. The tip opens
// for a mouse over the padlock, for keyboard focus, and for a click or a tap, and Escape puts it away. The real API answers the status.

let api: RealApi;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
});

test.afterAll(async () => {
  await api.dispose();
});

const padlock = (page: Page): Locator => page.getByRole('button', { name: 'Question content encryption' });
const tip = (page: Page): Locator => page.getByRole('tooltip');

test.beforeEach(async ({ page }) => {
  await api.openAdminPage(page, '/admin/questions');
  await expect(padlock(page)).toBeVisible();
});

test.describe('the encryption padlock on the question bank', () => {
  test('shows only a padlock until the details are asked for, and a mouse over it asks', async ({ page }) => {
    await expect(tip(page)).toBeHidden();
    await expect(page.getByText('Question text, options, answers and version history')).toBeHidden();

    await padlock(page).hover();

    await expect(tip(page)).toBeVisible();
    await expect(tip(page)).toContainText('Question content encryption');
    await expect(tip(page)).toContainText('Question text, options, answers and version history are encrypted');

    // The pointer can cross to the tip without it closing, and leaving both closes it.
    await tip(page).hover();
    await expect(tip(page)).toBeVisible();
    await page.mouse.move(2, 2);
    await expect(tip(page)).toBeHidden();
  });

  test('opens for keyboard focus, and Escape puts it away', async ({ page }) => {
    // From the heading, the next thing the Tab key reaches is the padlock.
    await page.getByRole('heading', { name: 'Question bank' }).evaluate((heading) => {
      heading.tabIndex = -1;
      heading.focus();
    });
    await page.keyboard.press('Tab');
    await expect(padlock(page)).toBeFocused();
    await expect(tip(page)).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(tip(page)).toBeHidden();
    await expect(padlock(page)).toBeFocused();
  });

  test('opens and closes with a click, and a click elsewhere closes it', async ({ page }) => {
    await padlock(page).click();
    await expect(tip(page)).toBeVisible();

    await page.getByRole('heading', { name: 'Question bank' }).click();
    await page.mouse.move(2, 2);
    await expect(tip(page)).toBeHidden();
  });

  test.describe('on a phone', () => {
    test.use({ viewport: { width: 390, height: 844 } });

    test('opens wholly on the screen, without the page scrolling sideways', async ({ page }) => {
      // On a phone the page's own menu is above the heading, so the padlock is scrolled to the top of the screen first, as a reader would.
      await padlock(page).evaluate((button) => button.scrollIntoView({ block: 'start' }));
      await padlock(page).click();

      await expect(tip(page)).toBeInViewport({ ratio: 1 });
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(0);
      const box = await padlock(page).boundingBox();
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(43.5);
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(43.5);
    });
  });
});
