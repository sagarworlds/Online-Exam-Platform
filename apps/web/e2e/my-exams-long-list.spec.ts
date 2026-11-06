import { expect, test } from './support/fixtures';
import { candidateAccessToken } from './support/fake-api';
import { text } from './support/messages';
import type { AttemptSummaryDto, MyExamDto } from '../src/app/candidate/candidate.models';

// A candidate with a long list of exams: the newest is first, and they can type, press a filter or choose an order to find the one they
// came for instead of scrolling. On a desktop and on a phone, where the controls must wrap inside the screen and be easy to hit.

const day = 86_400_000;
const hour = 3_600_000;
const at = (days: number) => new Date(Date.now() + days * day).toISOString();

function exam(overrides: Partial<MyExamDto> & { examId: string; name: string }): MyExamDto {
  return {
    description: null, startUtc: at(-1), endUtc: at(5), lateEntryDeadlineUtc: null, durationSeconds: 3600, questionCount: 30, state: 'Open',
    attemptId: null, attemptStatus: null, score: null, maxScore: null, attemptsAllowed: 1, attemptsUsed: 0, canStartAttempt: true, attempts: [],
    canRequestAttempt: false, attemptRequest: null, rules: null, ...overrides,
  };
}

const submitted = (id: string): AttemptSummaryDto => ({
  id, number: 1, status: 'Submitted', startedAtUtc: at(-9), submittedAtUtc: at(-9), autoSubmitted: false, score: 20, maxScore: 25,
});

const subjects = ['Maths', 'Science', 'English', 'History', 'Geography', 'Civics'];

// Ten exams to take, each made a day after the one before, so the last is the newest ("History Test 10"). The first closes in six hours.
const toTake = Array.from({ length: 10 }, (_, index) =>
  exam({
    examId: `t${index}`,
    name: `${subjects[index % subjects.length]} Test ${index + 1}`,
    createdAtUtc: at(-30 + index),
    endUtc: index === 0 ? new Date(Date.now() + 6 * hour).toISOString() : at(3 + index),
  }),
);

// Three already done, listed first, the way the API lists them.
const done = ['Old Maths Paper', 'Old Science Paper', 'Old English Paper'].map((name, index) =>
  exam({ examId: `d${index}`, name, state: 'Closed', startUtc: at(-20), endUtc: at(-15), canStartAttempt: false, attemptsUsed: 1, createdAtUtc: at(-60 + index), attempts: [submitted(`a${index}`)] }),
);

const exams: MyExamDto[] = [...done, ...toTake];

test.beforeEach(async ({ page, api }) => {
  void api;
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), ['exam-platform.auth-token', candidateAccessToken('candidate-1', 'session-1')] as [string, string]);
  await page.route('**/api/v1/me/exams', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(exams) }));
  await page.goto('/my-exams');
  await expect(page.getByRole('heading', { name: text('myExams.section.available') })).toBeVisible();
});

type Page = import('@playwright/test').Page;

const search = (page: Page) => page.getByRole('searchbox', { name: text('myExams.search.label') });
const cards = (page: Page) => page.locator('article .exam-card__name');
const chip = (page: Page, key: 'myExams.filter.closingSoon' | 'myExams.filter.done' | 'myExams.filter.all') =>
  page.locator('.exam-controls__chip', { hasText: text(key) });

test.describe('a long list of exams', () => {
  test('puts the exam made most recently first, above the others to take', async ({ page }) => {
    await expect(cards(page).first()).toHaveText('History Test 10');
    await expect(cards(page).nth(1)).toHaveText('English Test 9');
  });

  test('finds an exam by typing, says how many were found, and lists everything again when cleared', async ({ page }) => {
    await search(page).fill('science');

    await expect(cards(page)).toHaveText(['Science Test 8', 'Science Test 2', 'Old Science Paper']);
    await expect(page.getByRole('status').filter({ hasText: text('myExams.results', { shown: 3, total: 13 }) })).toBeVisible();

    await search(page).fill('');
    await expect(cards(page).first()).toHaveText('History Test 10');
    await expect(page.locator('.exam-controls__results')).toHaveText('');
  });

  test('narrows to the exams closing soon with one press, and back to all with another', async ({ page }) => {
    await chip(page, 'myExams.filter.closingSoon').click();

    await expect(cards(page)).toHaveText(['Maths Test 1']);
    await expect(chip(page, 'myExams.filter.closingSoon')).toHaveAttribute('aria-pressed', 'true');

    await chip(page, 'myExams.filter.all').click();
    await expect(cards(page).first()).toHaveText('History Test 10');
  });

  test('shows the done exams, which are folded away on the page, as soon as the candidate asks for them', async ({ page }) => {
    await expect(page.getByText('Old Maths Paper')).toHaveCount(0);

    await chip(page, 'myExams.filter.done').click();

    await expect(cards(page)).toHaveText(['Old English Paper', 'Old Science Paper', 'Old Maths Paper']);
  });

  test('puts the exams in the order chosen', async ({ page }) => {
    await page.getByLabel(text('myExams.sort.label')).selectOption({ label: text('myExams.sort.name') });

    await expect(cards(page).first()).toHaveText('Civics Test 6');
    await expect(cards(page).nth(1)).toHaveText('English Test 3');
    // Numbers are read as numbers: Test 4 comes before Test 10.
    await expect(cards(page).nth(4)).toHaveText('History Test 4');
    await expect(cards(page).nth(5)).toHaveText('History Test 10');
  });
});

test.describe('a long list of exams on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('does not scroll sideways, and keeps every control inside the screen and easy to hit', async ({ page }) => {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'the page is no wider than the screen').toBeLessThanOrEqual(0);

    const controls = page.locator('.exam-controls input, .exam-controls select, .exam-controls button').filter({ visible: true });
    const boxes = await controls.evaluateAll((elements) =>
      elements.map((element) => {
        const { left, right, height } = element.getBoundingClientRect();
        return { left, right, height };
      }),
    );
    // The search, the order and at least "All", "Closing soon" and "Done".
    expect(boxes.length).toBeGreaterThanOrEqual(5);
    for (const box of boxes) {
      expect(box.height).toBeGreaterThanOrEqual(43.5);
      expect(box.left).toBeGreaterThanOrEqual(0);
      expect(box.right).toBeLessThanOrEqual(390);
    }
  });

  test('finds an exam by typing', async ({ page }) => {
    await search(page).fill('geography');

    await expect(cards(page)).toHaveText(['Geography Test 5']);
  });
});
