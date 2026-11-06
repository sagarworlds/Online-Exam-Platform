import { randomUUID } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { text } from '../support/messages';
import { RealApi, type PublishedExam } from './real-api';

// A direct sign-in link against the real API and a real database: an administrator ticks the box when inviting, and the student opens the
// link in a browser with nothing else. No one-time code is typed and no account is made first.

const run = randomUUID().slice(0, 8);

let api: RealApi;
let exam: PublishedExam;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
  exam = await api.publishExam(`Direct link check ${run}`, [{ text: `What is two plus two? (${run})`, right: 'Four', wrong: 'Five' }]);
});

test.afterAll(async () => {
  await api.dispose();
});

test('opening a direct sign-in link signs a new student in and lands on the start page of their exam', async ({ page }) => {
  const link = await api.createDirectInvite(exam, `direct-${randomUUID().replaceAll('-', '')}@e2e.test`);
  expect(link).toContain('/invite/direct#code=');

  await page.goto(link);

  // The student is signed in and on the exam's start page, where the instructions and the proctoring notice still wait for them.
  await expect(page).toHaveURL(new RegExp(`/my-exams/${exam.id}/start$`));
  await expect(page.getByLabel(text('instructions.acknowledge'))).toBeVisible();

  // The one-time code is no longer on the address: it must not be left in the address bar or the history.
  expect(page.url()).not.toContain('code=');
});

test('a second visit to the same link says it was used, and offers to sign in another way', async ({ page }) => {
  const link = await api.createDirectInvite(exam, `direct-${randomUUID().replaceAll('-', '')}@e2e.test`);
  await page.goto(link);
  await expect(page).toHaveURL(new RegExp(`/my-exams/${exam.id}/start$`));

  // A fresh browser context would be the other device; the same page is enough to show the link works once.
  await page.evaluate(() => localStorage.clear());
  await page.goto(link);

  await expect(page.getByRole('alert')).toContainText(text('inviteDirect.invalid'));
  await expect(page.getByRole('link', { name: text('inviteDirect.signIn'), exact: true })).toBeVisible();
});
