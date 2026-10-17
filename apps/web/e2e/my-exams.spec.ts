import { expect, test } from './support/fixtures';
import { candidateAccessToken } from './support/fake-api';
import { text } from './support/messages';
import type { AttemptSummaryDto, MyExamDto } from '../src/app/candidate/candidate.models';

// The candidate's exams page, listed the way the API lists them: attempted exams first and the newest exam last. What the candidate looks
// for is the exam they have not taken, so the page puts it first and folds the attempted exams away at the bottom, on a desktop and on a phone.

const day = 86_400_000;
const at = (days: number) => new Date(Date.now() + days * day).toISOString();

function exam(overrides: Partial<MyExamDto> & { examId: string; name: string }): MyExamDto {
  return {
    description: null, startUtc: at(-1), endUtc: at(2), lateEntryDeadlineUtc: null, durationSeconds: 3600, questionCount: 30, state: 'Open',
    attemptId: null, attemptStatus: null, score: null, maxScore: null, attemptsAllowed: 1, attemptsUsed: 0, canStartAttempt: true, attempts: [],
    canRequestAttempt: false, attemptRequest: null, rules: null, ...overrides,
  };
}

const submitted = (id: string, number: number, score: number, max: number): AttemptSummaryDto => ({
  id, number, status: 'Submitted', startedAtUtc: at(-9), submittedAtUtc: at(-9), autoSubmitted: false, score, maxScore: max,
});

const exams: MyExamDto[] = [
  exam({ examId: 'e1', name: 'English Grammar Test', state: 'Closed', startUtc: at(-9), endUtc: at(-5), canStartAttempt: false, attemptsUsed: 1, attempts: [submitted('a1', 1, 42, 50)] }),
  exam({ examId: 'e2', name: 'History Chapter 5 Revision Paper', state: 'Closed', startUtc: at(-8), endUtc: at(-4), attemptsAllowed: 2, attemptsUsed: 2, canStartAttempt: false, attempts: [submitted('a2', 1, 30, 40), submitted('a3', 2, 35, 40)] }),
  exam({ examId: 'e3', name: 'Biology Quiz', state: 'NotOpen', startUtc: at(1), endUtc: at(2), canStartAttempt: false }),
  exam({ examId: 'e4', name: 'Geography Test', state: 'Closed', startUtc: at(-10), endUtc: at(-6), canStartAttempt: false, attemptsUsed: 1, attempts: [submitted('a4', 1, 18, 25)] }),
  exam({ examId: 'e5', name: 'Chemistry Mock 2' }),
];

test.beforeEach(async ({ page, api }) => {
  void api;
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), ['exam-platform.auth-token', candidateAccessToken('candidate-1', 'session-1')] as [string, string]);
  await page.route('**/api/v1/me/exams', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(exams) }));
  await page.goto('/my-exams');
  await expect(page.getByRole('heading', { name: text('myExams.section.available') })).toBeVisible();
});

const toggle = (page: import('@playwright/test').Page) => page.getByRole('button', { name: new RegExp(`^${text('myExams.section.attempted', { count: 3 }).replace(/[()]/g, '\\$&')}`) });
const attemptedCard = (page: import('@playwright/test').Page) => page.getByRole('heading', { name: /Geography Test/ });

test.describe('the exams page', () => {
  test('puts the new exam above the exams already attempted, and folds those away at the bottom', async ({ page }) => {
    const newExam = page.getByRole('heading', { name: /Chemistry Mock 2/ });
    const comingUp = page.getByRole('heading', { name: text('myExams.section.upcoming') });

    await expect(newExam).toBeVisible();
    await expect(toggle(page)).toHaveAttribute('aria-expanded', 'false');
    await expect(attemptedCard(page)).toHaveCount(0);

    const newY = (await newExam.boundingBox())?.y ?? Number.NaN;
    const comingY = (await comingUp.boundingBox())?.y ?? Number.NaN;
    const toggleY = (await toggle(page).boundingBox())?.y ?? Number.NaN;
    expect(newY).toBeLessThan(comingY);
    expect(comingY).toBeLessThan(toggleY);
  });

  test('opens and closes the attempted exams without moving their heading, or the new exams above it', async ({ page }) => {
    const before = await toggle(page).boundingBox();
    const newBefore = await page.getByRole('heading', { name: /Chemistry Mock 2/ }).boundingBox();

    await toggle(page).click();
    await expect(toggle(page)).toHaveAttribute('aria-expanded', 'true');
    await expect(attemptedCard(page)).toBeVisible();
    expect(await toggle(page).boundingBox()).toEqual(before);
    expect(await page.getByRole('heading', { name: /Chemistry Mock 2/ }).boundingBox()).toEqual(newBefore);

    await toggle(page).click();
    await expect(attemptedCard(page)).toHaveCount(0);
    expect(await toggle(page).boundingBox()).toEqual(before);
  });

  test('keeps the choice to open the attempted exams when the page is read again', async ({ page }) => {
    await toggle(page).click();
    await page.reload();

    await expect(toggle(page)).toHaveAttribute('aria-expanded', 'true');
    await expect(attemptedCard(page)).toBeVisible();
  });
});

test.describe('the exams page on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('does not scroll sideways, and every action is easy to hit', async ({ page }) => {
    await toggle(page).click();
    await expect(attemptedCard(page)).toBeVisible();
    await page.screenshot({ path: '/tmp/claude-0/-home-user-Online-Exam-Platform/00a129c1-56f2-5785-a000-17d992abe0f6/scratchpad/myexams/expanded-mobile.png', fullPage: true });

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'the page is no wider than the screen').toBeLessThanOrEqual(0);

    const controls = page.locator('article a.btn, article button, .exam-section__toggle');
    const heights = await controls.evaluateAll((elements) => elements.map((element) => element.getBoundingClientRect().height));
    expect(heights.length).toBeGreaterThan(3);
    for (const height of heights) {
      expect(height).toBeGreaterThanOrEqual(43.5);
    }
  });

  test('gives the one thing to do the width of the card', async ({ page }) => {
    const start = page.getByRole('link', { name: text('myExams.start') });
    const card = page.locator('article', { has: page.getByRole('heading', { name: /Chemistry Mock 2/ }) });

    const [button, container] = await Promise.all([start.first().boundingBox(), card.boundingBox()]);
    expect(button?.width ?? 0).toBeGreaterThan((container?.width ?? 0) - 40);
  });

  test('keeps a score in one piece, with its words, rather than splitting it across lines', async ({ page }) => {
    await toggle(page).click();

    const score = page.locator('article', { has: page.getByRole('heading', { name: /Geography Test/ }) }).locator('.nowrap');
    await expect(score).toContainText('18 / 25');
    // One line of text: the span is no taller than a line and a half of its own text.
    const [height, lineHeight] = await score.evaluate((element) => [element.getBoundingClientRect().height, Number.parseFloat(getComputedStyle(element).lineHeight)]);
    expect(height).toBeLessThan(lineHeight * 1.5);
  });
});
