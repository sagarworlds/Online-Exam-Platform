import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi, type EnrolledCandidate, type PublishedExam } from './real-api';

// An exam's Candidates and attempts page against the real API and a real database: a candidate is found by the phone number they registered
// with, however it was written then and however staff write it now, and the page never holds a number. A candidate is also invited to the
// exam from the page, and the invitation is the API's own.

const run = randomUUID().slice(0, 8);

/** Nine digits of this run behind a leading 9, so the number is new to a database that keeps what earlier runs left. */
const mobile = (seed: string) => `9${(Number.parseInt(seed + run.slice(0, 7), 16) % 1_000_000_000).toString().padStart(9, '0')}`;

let api: RealApi;
let exam: PublishedExam;
let amy: EnrolledCandidate;
let bob: EnrolledCandidate;
// Amy registered with her number written with a country code and a dash; Bob wrote his plainly.
let amyNumber: string;
let bobNumber: string;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
  exam = await api.publishExam(`Phone search ${run}`, [{ text: `Which colour is grass here? (${run})`, right: 'Green', wrong: 'Red' }]);
  const amyDigits = mobile('a');
  const bobDigits = mobile('b');
  amyNumber = `+91 ${amyDigits.slice(0, 5)}-${amyDigits.slice(5)}`;
  bobNumber = bobDigits;
  amy = await api.enrolNewCandidate(exam, amyNumber);
  bob = await api.enrolNewCandidate(exam, bobNumber);
});

test.afterAll(async () => {
  await api.dispose();
});

async function openPage(page: Page): Promise<void> {
  await api.openAdminPage(page, `/exams/${exam.id}/attempts`);
  await expect(page.getByRole('heading', { name: 'Candidates and attempts', level: 1 })).toBeVisible();
}

const search = (page: Page): Locator => page.getByRole('searchbox', { name: 'Search candidates by email or phone number' });
const cards = (page: Page): Locator => page.locator('.card__title');

test.describe('finding a candidate by phone number', () => {
  test('finds the candidate whichever way the number is written, and the page never holds a number', async ({ page }) => {
    await openPage(page);
    await expect(cards(page)).toHaveCount(2);

    const amyDigits = amyNumber.replace(/\D/g, '').slice(2);
    for (const typed of [amyDigits, `+91 ${amyDigits.slice(0, 5)} ${amyDigits.slice(5)}`, `0${amyDigits}`, amyDigits.slice(2, 8)]) {
      await search(page).fill(typed);
      await expect(cards(page)).toHaveCount(1);
      await expect(cards(page).first()).toContainText(amy.email);
    }

    await search(page).fill(`${bobNumber.slice(0, 5)}-${bobNumber.slice(5)}`);
    await expect(cards(page)).toHaveCount(1);
    await expect(cards(page).first()).toContainText(bob.email);

    await expect(page.locator('body')).not.toContainText(amyNumber.replace(/\D/g, '').slice(2, 8));
    await expect(page.locator('body')).not.toContainText(bobNumber.slice(0, 6));
  });

  test('finds no one for a number nobody here has, and still finds by email', async ({ page }) => {
    await openPage(page);

    await search(page).fill('1111111111');
    await expect(page.getByText('Nothing matches')).toBeVisible();

    await search(page).fill(bob.email.toUpperCase());
    await expect(cards(page)).toHaveCount(1);
    await expect(cards(page).first()).toContainText(bob.email);
  });
});

test.describe('inviting a candidate from the page', () => {
  test('sends an invitation for this exam, which the API then lists as pending', async ({ page }) => {
    const address = `invited-${run}@e2e.test`;
    await openPage(page);

    await page.getByRole('button', { name: 'Invite a candidate', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: `Invite a candidate to ${exam.name}` });
    await dialog.getByLabel("Candidate's email").fill(address);
    await dialog.getByRole('button', { name: 'Send invitation' }).click();

    // Whether the mail went out depends on how the API is set up; either way the invitation was made and the dialog says so.
    await expect(dialog.getByText(new RegExp(`Invitation (emailed to|created for) ${address.replace('.', '\.')}`))).toBeVisible();
    await expect(dialog.getByText('appear here once they accept')).toBeVisible();

    const invites = await api.invitesFor(exam);
    expect(invites.map((invite) => [invite.email, invite.status])).toContainEqual([address, 'Pending']);
  });
});
