import type { Page, Route } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { staffAccessToken } from './support/fake-api';
import { text } from './support/messages';
import type { ExamAttemptsDto, ExamCandidateDto } from '../src/app/attempt-management/attempt-admin.models';

// An exam's "Candidates and attempts" page, as staff use it: finding a candidate by email or by phone number, and inviting one to the exam
// without leaving the page. The API is replaced inside the browser; the phone numbers live only in this fake API's table, as they live only
// in the real one, so the page can never show one: it is told which candidates match.

const EXAM = 'exam-1';

const candidate = (candidateId: string, email: string, attempts: ExamCandidateDto['attempts'] = []): ExamCandidateDto => ({
  candidateId, email, attemptsAllowed: 1, attemptsUsed: attempts.length, canGrant: false, attempts,
});

const roster: ExamAttemptsDto = {
  examId: EXAM,
  examName: 'Maths Final',
  windowClosed: false,
  attemptsPerCandidate: 1,
  candidates: [candidate('c1', 'amy@example.com'), candidate('c2', 'bob@example.com'), candidate('c3', 'cy@example.com')],
};

/** The numbers as the candidates entered them: with spaces, dashes and a country code in some, and none in others. */
const numbers: Record<string, string> = { c1: '98111 22222', c2: '+91 98765-43210', c3: '09000011111' };

const digits = (value: string) => value.replace(/\D/g, '').replace(/^0+/, '');

/** What the real API answers, in the part this page uses: candidates whose number holds the digits typed, a country code or trunk 0 aside. */
function phoneMatches(typed: string): string[] {
  let wanted = digits(typed);
  if (typed.trim().startsWith('+') && wanted.startsWith('91')) {
    wanted = wanted.slice(2);
  }
  return Object.entries(numbers)
    .filter(([, stored]) => digits(stored).replace(/^91(?=\d{10}$)/, '').includes(wanted))
    .map(([id]) => id);
}

const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

async function openPage(page: Page, permissions: readonly string[] = ['exam.manage', 'invite.manage'], shown: ExamAttemptsDto = roster) {
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), ['exam-platform.auth-token', staffAccessToken('staff-1', 'session-1', permissions)] as [string, string]);
  await page.route(`**/api/v1/exams/${EXAM}/attempts`, (route) => json(route, shown));
  await page.route('**/api/v1/ai-grading/status', (route) => json(route, { enabled: false, waitingAnswers: 0 }));
  await page.goto(`/exams/${EXAM}/attempts`);
  await expect(page.getByRole('heading', { name: 'Candidates and attempts', level: 1 })).toBeVisible();
}

/** The phone-number asks the page made, so a test can check what was sent and where. */
function recordPhoneSearches(page: Page) {
  const asks: { url: string; body: unknown }[] = [];
  void page.route(`**/api/v1/exams/${EXAM}/candidates/phone-search`, (route) => {
    const body = route.request().postDataJSON() as { phone: string };
    asks.push({ url: route.request().url(), body });
    return json(route, { candidateIds: phoneMatches(body.phone) });
  });
  return asks;
}

const search = (page: Page) => page.getByRole('searchbox', { name: text('attempts.search') });
const cards = (page: Page) => page.locator('.card__title');
const inviteButton = (page: Page) => page.getByRole('button', { name: text('attempts.invite'), exact: true });

test.describe('finding a candidate', () => {
  test('by the email address, in any case', async ({ page }) => {
    await openPage(page);

    await search(page).fill('BOB@');

    await expect(cards(page)).toHaveCount(1);
    await expect(cards(page).first()).toContainText('bob@example.com');
  });

  test('by phone number, however the number is written, without the page ever holding one', async ({ page }) => {
    const asks = recordPhoneSearches(page);
    await openPage(page);

    // Bob entered "+91 98765-43210"; staff type it with other spacing, then without the country code, then a part of it.
    for (const typed of ['98765 43210', '+91 9876543210', '(098765) 43210', '65432']) {
      await search(page).fill(typed);
      await expect(cards(page)).toHaveCount(1);
      await expect(cards(page).first()).toContainText('bob@example.com');
    }

    // The number is in the body of the ask, not in an address, and nowhere on the page.
    expect(asks.length).toBeGreaterThan(0);
    for (const ask of asks) {
      expect(ask.url).not.toMatch(/\d{5}/);
    }
    await expect(page.locator('body')).not.toContainText('98765');
    await expect(page.locator('body')).not.toContainText('43210');
  });

  test('says that nobody matches a number that nobody has, once it has looked', async ({ page }) => {
    recordPhoneSearches(page);
    await openPage(page);

    await search(page).fill('77777 77777');

    await expect(page.getByText(text('list.noMatch'))).toBeVisible();
    await expect(cards(page)).toHaveCount(0);
  });

  test('does not look up an email address or a few digits as a phone number', async ({ page }) => {
    const asks = recordPhoneSearches(page);
    await openPage(page);

    await search(page).fill('cy');
    await expect(cards(page)).toHaveCount(1);
    await search(page).fill('98');
    await page.waitForTimeout(600);

    expect(asks).toEqual([]);
  });
});

test.describe('inviting a candidate from the page', () => {
  test('is offered to staff who may invite, and not to those who may not', async ({ page }) => {
    await openPage(page, ['exam.manage']);

    await expect(inviteButton(page)).toHaveCount(0);
  });

  test('sends the invitation for this exam, says what happened, and gives the page back where it was', async ({ page }) => {
    const sent: unknown[] = [];
    await page.route('**/api/v1/invites', (route) => {
      sent.push(route.request().postDataJSON());
      return json(route, { id: 'i1', examId: EXAM, email: 'new.person@example.com', status: 'Pending', emailSent: true, whatsAppSent: false, inviteLink: null }, 201);
    });
    await openPage(page);
    await search(page).fill('amy');

    await inviteButton(page).click();
    const dialog = page.getByRole('dialog', { name: text('attempts.invite.title', { exam: 'Maths Final' }) });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(text('invites.create.email')).fill('new.person@example.com');
    await dialog.getByRole('button', { name: text('invites.create.send') }).click();

    await expect(dialog.getByText(text('invites.create.emailed', { email: 'new.person@example.com' }))).toBeVisible();
    await expect(dialog.getByText(text('attempts.invite.appearsHint'))).toBeVisible();
    expect(sent).toEqual([{ examId: EXAM, email: 'new.person@example.com' }]);

    await dialog.getByRole('button', { name: text('common.close') }).click();
    await expect(dialog).toBeHidden();
    // Back on the page as it was: the search is still there, and focus is back on the button that opened the popup.
    await expect(search(page)).toHaveValue('amy');
    await expect(inviteButton(page)).toBeFocused();
  });

  test('says why when the invitation is refused, and keeps what was typed', async ({ page }) => {
    await page.route('**/api/v1/invites', (route) => json(route, { title: 'invite_exam_not_found', detail: 'The exam is not open for invitations.' }, 404));
    await openPage(page);

    await inviteButton(page).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(text('invites.create.email')).fill('new.person@example.com');
    await dialog.getByRole('button', { name: text('invites.create.send') }).click();

    await expect(dialog.getByRole('alert')).toContainText('The exam is not open for invitations.');
    await expect(dialog.getByLabel(text('invites.create.email'))).toHaveValue('new.person@example.com');
  });
});

test.describe('the page on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  /** Tall enough to touch with a thumb: WCAG 2.5.8 asks 24px and this app asks 44. */
  const expectEasyToHit = async (target: import('@playwright/test').Locator, what: string) => {
    const box = (await target.boundingBox())!;
    expect(box.height, `${what} is at least 44px tall`).toBeGreaterThanOrEqual(43.5);
  };
  const scrollsSideways = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth > 0);

  test('fits the width, with the search and the invite button easy to hit', async ({ page }) => {
    recordPhoneSearches(page);
    await openPage(page);
    await search(page).fill('98765');
    await expect(cards(page)).toHaveCount(1);

    expect(await scrollsSideways(page), 'the page is no wider than the screen').toBe(false);
    await expectEasyToHit(search(page), 'the search box');
    await expectEasyToHit(inviteButton(page), 'the invite button');
  });

  test('keeps a candidate card inside the screen when the address and the status do not fit side by side, whatever the font', async ({ page }) => {
    // An address that fits a phone card alone, in any font, but not beside "0 of 1 attempt used": the status has to go below it. A machine whose
    // font is narrower hides the fault by fitting both on one line, which is how this reached the main branch; this address does not let it.
    const long = candidate('c9', 'candidate.name@school.example.org');
    await openPage(page, ['exam.manage', 'invite.manage'], { ...roster, candidates: [long] });
    await expect(cards(page)).toHaveCount(1);

    expect(await scrollsSideways(page), 'the page is no wider than the screen').toBe(false);
    const card = (await page.locator('.card').first().boundingBox())!;
    const badge = (await page.locator('.card__title .badge').first().boundingBox())!;
    expect(badge.x + badge.width, 'the status stays inside its card').toBeLessThanOrEqual(card.x + card.width);
  });

  test('has an invite popup that fits the width, with every control easy to hit', async ({ page }) => {
    await openPage(page, ['exam.manage', 'invite.manage', 'invite.direct-link']);

    await inviteButton(page).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    expect(await scrollsSideways(page), 'the page is no wider than the screen').toBe(false);
    const box = (await dialog.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(390);
    await expectEasyToHit(dialog.getByLabel(text('invites.create.email')), 'the email box');
    // The tick box is a few pixels; what is touched is its label, which ticks it, so that is what has to be tall enough.
    await expectEasyToHit(dialog.locator('label', { hasText: text('invites.create.direct') }), 'the direct sign-in label');
    await expectEasyToHit(dialog.getByRole('button', { name: text('invites.create.send') }), 'the send button');
    await expectEasyToHit(dialog.getByRole('button', { name: text('common.close') }), 'the close button');
  });
});
