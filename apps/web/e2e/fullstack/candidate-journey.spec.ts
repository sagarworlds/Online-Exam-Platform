import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { text } from '../support/messages';
import { RealApi, type EnrolledCandidate, type PublishedExam, type QuestionSpec } from './real-api';

// The same candidate journey as the stubbed tests, against the real API and a real database. What these add is that the page and the API
// agree: what the page sends is what the API accepts, and what the API holds is what the page shows. Nothing is stubbed. The sign-in code
// is the one the API writes to its log (e2e/fullstack/real-api.ts).

const run = randomUUID().slice(0, 8);

const questions: QuestionSpec[] = [
  { text: `Which colour is the sky on a clear day? (${run})`, right: 'Blue', wrong: 'Green' },
  { text: `What is two plus two? (${run})`, right: 'Four', wrong: 'Five' },
];

let api: RealApi;
let exam: PublishedExam;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
  exam = await api.publishExam(`Full stack check ${run}`, questions);
});

test.afterAll(async () => {
  await api.dispose();
});

/** Signs in on the log-in page with a one-time code: asks for it, reads it from the API's log, and types it in. */
async function signInWithCode(page: Page, candidate: EnrolledCandidate, code?: string): Promise<void> {
  await page.goto('/login');
  await page.getByRole('button', { name: text('login.mode.otp'), exact: true }).click();
  await page.getByLabel(text('login.destination'), { exact: true }).fill(candidate.email);

  const mark = api.signInCodes.mark();
  await page.getByRole('button', { name: text('login.sendCode'), exact: true }).click();
  await expect(page).toHaveURL(/\/verify-otp/);

  const sent = await api.signInCodes.nextAfter(mark);
  await page.getByLabel(text('verify.code'), { exact: true }).fill(code ?? sent);
  await page.getByRole('button', { name: text('verify.submit'), exact: true }).click();
}

/** From the exams page to the first question: read the instructions, tick the box and start. */
async function startExam(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/my-exams$/);
  await page.getByRole('link', { name: text('myExams.start'), exact: true }).click();

  // The system check asks the real API's health endpoint three times.
  for (const check of ['Browser', 'Internet connection', 'Exam server']) {
    await expect(page.getByRole('listitem').filter({ has: page.locator('strong', { hasText: check }) })).toContainText('Passed');
  }
  await page.getByLabel(text('instructions.acknowledge')).check();
  await page.getByRole('button', { name: text('instructions.start'), exact: true }).click();
  await expect(page).toHaveURL(/\/attempt\/[0-9a-f-]+$/);
}

/** The question the page shows now: the exam may put them in any order, so it is found by its text. */
async function shownQuestion(page: Page): Promise<QuestionSpec> {
  await expect(page.locator('.question__text')).toBeVisible();
  const shown = await page.locator('.question__text').innerText();
  const question = questions.find((candidate) => shown.includes(candidate.text));
  if (question === undefined) {
    throw new Error(`The page shows a question that is not one of this exam's: ${shown}`);
  }
  return question;
}

/** Chooses an answer the way a candidate does, by clicking its visible label. */
async function choose(page: Page, answer: string): Promise<void> {
  await page.locator('label.option-choice', { hasText: answer }).click();
  await expect(page.getByRole('radio', { name: answer })).toBeChecked();
}

/** The token the page holds for the signed-in candidate: what the app itself sends the API (see AuthSessionService). */
async function tokenHeldBy(page: Page): Promise<string> {
  const token = await page.evaluate(() => localStorage.getItem('exam-platform.auth-token'));
  expect(token, 'the page holds a session token after signing in').not.toBeNull();
  return token ?? '';
}

function attemptIdOf(page: Page): string {
  return new URL(page.url()).pathname.split('/').pop() ?? '';
}

test.describe('a candidate sitting an exam against the real API', () => {
  test('signs in with a one-time code, answers one right and one wrong, submits, and the score is the API\'s', async ({ page }) => {
    const candidate = await api.enrolNewCandidate(exam);
    await signInWithCode(page, candidate);
    await startExam(page);

    // Answer whichever question comes first right, and the other wrong, whatever order the exam shows them in.
    const first = await shownQuestion(page);
    await choose(page, first.right);
    await page.getByRole('button', { name: text('attempt.saveNext'), exact: false }).click();
    const second = await shownQuestion(page);
    expect(second.text).not.toBe(first.text);
    await choose(page, second.wrong);

    await page.getByRole('button', { name: text('attempt.submit.ask'), exact: true }).click();
    await page.getByRole('button', { name: text('attempt.submit.yes'), exact: true }).click();

    await expect(page.getByText('1 / 2')).toBeVisible();

    // The API agrees: one attempt, submitted, one mark of two.
    const [held] = await api.candidateExams(await tokenHeldBy(page));
    expect(held).toMatchObject({ attemptStatus: 'Submitted', score: 1, maxScore: 2 });
  });

  test('numbers each answer above the one the API holds, and a reload brings the answer back', async ({ page }) => {
    const candidate = await api.enrolNewCandidate(exam);
    await signInWithCode(page, candidate);
    await startExam(page);
    const attemptId = attemptIdOf(page);
    const token = await tokenHeldBy(page);

    const question = await shownQuestion(page);
    await choose(page, question.wrong);
    await expect
      .poll(async () => (await api.attempt(token, attemptId)).sections[0]?.questions.find((held) => held.text.includes(question.text))?.answerSequence)
      .toBe(1);

    await page.reload();
    await expect(page.getByRole('radio', { name: question.wrong })).toBeChecked();

    // After the reload the page numbers its next write above the one the real API holds, so the change is applied rather than refused.
    await choose(page, question.right);
    await expect
      .poll(async () => {
        const held = (await api.attempt(token, attemptId)).sections[0]?.questions.find((q) => q.text.includes(question.text));
        return [held?.answerSequence, held?.selectedOptionId === null];
      })
      .toEqual([2, false]);
  });

  test('a wrong code is refused by the API, and the page shows its reason and stays on the code page', async ({ page }) => {
    const candidate = await api.enrolNewCandidate(exam);
    await page.goto('/login');
    await page.getByRole('button', { name: text('login.mode.otp'), exact: true }).click();
    await page.getByLabel(text('login.destination'), { exact: true }).fill(candidate.email);
    const mark = api.signInCodes.mark();
    await page.getByRole('button', { name: text('login.sendCode'), exact: true }).click();
    await expect(page).toHaveURL(/\/verify-otp/);

    // A six-digit code that is not the one sent.
    const sent = await api.signInCodes.nextAfter(mark);
    const wrong = sent === '000000' ? '000001' : '000000';
    await page.getByLabel(text('verify.code'), { exact: true }).fill(wrong);
    await page.getByRole('button', { name: text('verify.submit'), exact: true }).click();

    await expect(page.getByRole('alert')).not.toBeEmpty();
    await expect(page).toHaveURL(/\/verify-otp/);
  });
});
