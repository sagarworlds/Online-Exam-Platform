import { expect, type Page } from '@playwright/test';
import type { FakeApi } from './fake-api';
import { text } from './messages';

// The steps of a candidate's way into an exam, shared by the specs that start from a question on the page.

async function signIn(page: Page, api: FakeApi): Promise<void> {
  await page.goto('/login');
  await page.getByLabel(text('login.email'), { exact: true }).fill('candidate@example.com');
  await page.getByLabel(text('login.password'), { exact: true }).fill(api.password);
  await page.getByRole('button', { name: text('login.submit'), exact: true }).click();
  await expect(page).toHaveURL(/\/my-exams$/);
}

/** Signs in, reads the instructions, ticks the box and starts the exam; ends on the first question. */
export async function startExam(page: Page, api: FakeApi): Promise<void> {
  await signIn(page, api);
  await page.getByRole('link', { name: text('myExams.start'), exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/my-exams/${api.examId}/start$`));

  // The system check looks at this browser and the server before the start button can be used. The connection-speed line depends on the
  // network the test runs on, so only the three lines that must pass are checked, and no problem may be reported.
  for (const check of ['Browser', 'Internet connection', 'Exam server']) {
    await expect(checkLine(page, check)).toContainText('Passed');
  }
  await expect(page.getByRole('alert').filter({ hasText: text('instructions.checkProblem') })).toHaveCount(0);
  const start = page.getByRole('button', { name: text('instructions.start'), exact: true });
  await expect(start).toBeDisabled();
  await page.getByLabel(text('instructions.acknowledge')).check();
  await expect(start).toBeEnabled();
  await start.click();

  await expect(page).toHaveURL(new RegExp(`/attempt/${api.attemptId}$`));
  // Whatever the first question says: a formula in it is typeset, so its text is not found as written.
  await expect(page.locator('.question__text')).toBeVisible();
}

/** One line of the system check, by what it looked at. */
function checkLine(page: Page, name: string) {
  return page.getByRole('listitem').filter({ has: page.locator('strong', { hasText: name }) });
}

/** The radio button for an answer, to read its state. */
export function optionOf(page: Page, answer: string) {
  return page.getByRole('radio', { name: answer });
}

/** Chooses an answer the way a candidate does, by clicking its visible label: the native radio button is drawn over by a marker. */
export async function choose(page: Page, answer: string): Promise<void> {
  await page.locator('label.option-choice', { hasText: answer }).click();
  await expect(optionOf(page, answer)).toBeChecked();
}
