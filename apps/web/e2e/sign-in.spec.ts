import type { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { text } from './support/messages';

const email = 'candidate@example.com';

/** The password field by its label. `exact`, because the show/hide button's name also contains the word "password" on some engines. */
function passwordField(page: Page) {
  return page.getByLabel(text('login.password'), { exact: true });
}

async function fillAndSubmit(page: Page, password: string): Promise<void> {
  await page.getByLabel(text('login.email'), { exact: true }).fill(email);
  await passwordField(page).fill(password);
  await page.getByRole('button', { name: text('login.submit'), exact: true }).click();
}

test.describe('signing in', () => {
  test('a signed-out visitor to the site root is sent to the log-in page', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('heading', { level: 1, name: text('login.title') })).toBeVisible();
  });

  test('submitting nothing from the keyboard marks each empty field invalid and moves focus to the first', async ({ page }) => {
    await page.goto('/login');
    const emailField = page.getByLabel(text('login.email'), { exact: true });

    // Enter in a field submits the form, as it does for a person who never reaches for the mouse.
    await emailField.focus();
    await page.keyboard.press('Enter');

    for (const field of [emailField, passwordField(page)]) {
      await expect(field).toHaveAttribute('aria-invalid', 'true');
      // The message the field points at is on the page and says something.
      const describedBy = await field.getAttribute('aria-describedby');
      await expect(page.locator(`#${describedBy}`)).not.toBeEmpty();
    }
    await expect(emailField).toBeFocused();
  });

  test('a wrong password shows the reason as an alert and stays on the page', async ({ page, api }) => {
    await page.goto('/login');

    await fillAndSubmit(page, 'not-the-password');

    await expect(page.getByRole('alert')).toHaveText('The email or password is incorrect.');
    await expect(page).toHaveURL(/\/login$/);
    expect(api.requestsTo('POST', /\/auth\/login$/)).toHaveLength(1);
  });

  test('the show and hide button reveals the password and hides it again', async ({ page }) => {
    await page.goto('/login');
    const field = passwordField(page);
    await field.fill('a secret');

    await page.getByRole('button', { name: text('login.showPassword'), exact: true }).click();
    await expect(field).toHaveAttribute('type', 'text');

    await page.getByRole('button', { name: text('login.hidePassword'), exact: true }).click();
    await expect(field).toHaveAttribute('type', 'password');
  });

  test('a candidate who signs in lands on their exams, and the token is kept for the next visit', async ({ page, api }) => {
    await page.goto('/login');

    await fillAndSubmit(page, api.password);

    await expect(page).toHaveURL(/\/my-exams$/);
    await expect(page.getByRole('heading', { level: 1, name: text('myExams.title') })).toBeVisible();

    // A second visit finds the session in storage and does not ask again.
    await page.goto('/');
    await expect(page).toHaveURL(/\/my-exams$/);
  });
});
