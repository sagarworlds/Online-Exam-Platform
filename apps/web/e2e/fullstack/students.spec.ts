import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi, type PublishedExam } from './real-api';

// The Students page brings the API's students and invitations together: the names, addresses and statuses come from two modules, and a
// change to a student's address or number has to carry the invitations and end the student's sessions, so the page and the API are
// tested together; nothing is stubbed.

const run = randomUUID().slice(0, 8);
// Each exam gets a question of its own wording: the bank refuses a second question that repeats one it has.
const questionFor = (subject: string) => ({ text: `Which colour is grass in ${subject}? (${run})`, right: 'Green', wrong: 'Red' });

let api: RealApi;
let maths: PublishedExam;
let physics: PublishedExam;

// Two invited students with no account, so the page has a row for each: Asha is invited to both exams, Ben to one. Every address carries
// this run's id, and the page is read through it alone, since the database holds whatever the other tests left.
const asha = `asha-${run}@e2e.test`;
const ben = `ben-${run}@e2e.test`;

test.beforeAll(async ({ playwright }) => {
  api = await RealApi.connect(playwright);
  maths = await api.publishExam(`Maths ${run}`, [questionFor('maths')]);
  physics = await api.publishExam(`Physics ${run}`, [questionFor('physics')]);
  await api.invite(maths, asha);
  await api.invite(physics, asha);
  await api.invite(maths, ben);
});

test.afterAll(async () => {
  await api.dispose();
});

/** Opens the page as the administrator and narrows it with a search, so only the rows the test made are in it. */
async function openStudents(page: Page, search: string): Promise<void> {
  await api.openAdminPage(page, '/students');
  // The page's own title: by name alone this also matches headings of the rows.
  await expect(page.getByRole('heading', { name: 'Students', level: 1 })).toBeVisible();
  await page.getByLabel('Search students').fill(search);
  await expect(page.locator('section.student-row').first()).toBeVisible();
}

const row = (page: Page, text: string): Locator => page.locator('section.student-row').filter({ hasText: text });
const toggle = (page: Page, text: string): Locator => row(page, text).locator('.student-row__toggle');
// The icons are named for the row's title, which is the student's name; a row is found by its address, so the name is matched loosely.
const editIcon = (page: Page, text: string): Locator => row(page, text).getByRole('button', { name: /^Edit / });
const deactivateIcon = (page: Page, text: string): Locator => row(page, text).getByRole('button', { name: /^Deactivate / });
const reactivateIcon = (page: Page, text: string): Locator => row(page, text).getByRole('button', { name: /^Reactivate / });
const editPopup = (page: Page): Locator => page.getByRole('dialog', { name: 'Edit profile' });

test.describe('the Students page', () => {
  test('shows an invited student with no account once, with both exam names, beside another', async ({ page }) => {
    await openStudents(page, run);

    await expect(row(page, asha)).toHaveCount(1);
    await expect(toggle(page, asha)).toContainText('Not registered yet');
    await expect(toggle(page, asha)).toContainText('2 invitations');
    await toggle(page, asha).click();
    const rows = row(page, asha).locator('.invite-row');
    await expect(rows).toHaveCount(2);
    // Newest first: Physics was invited second.
    await expect(rows.nth(0)).toContainText(physics.name);
    await expect(rows.nth(1)).toContainText(maths.name);

    await toggle(page, ben).click();
    await expect(row(page, ben).locator('.invite-row')).toHaveCount(1);
    await expect(row(page, ben)).not.toContainText(physics.name);
  });

  test('opens and folds a student on request', async ({ page }) => {
    await openStudents(page, run);
    const header = toggle(page, asha);

    await expect(header).toHaveAttribute('aria-expanded', 'false');
    await header.click();
    await expect(header).toHaveAttribute('aria-expanded', 'true');
    await expect(row(page, asha).locator('.invite-row')).toHaveCount(2);

    await header.click();
    await expect(header).toHaveAttribute('aria-expanded', 'false');
    await expect(row(page, asha).locator('.invite-row')).toHaveCount(0);
    // Folded, the heading still says who it is and what is in it.
    await expect(header).toContainText(asha);
    await expect(header).toContainText('2 Pending');
  });

  test('keeps a student open after one of their invitations is revoked, and shows it revoked', async ({ page }) => {
    const solo = `solo-${run}-revoke@e2e.test`;
    await api.invite(maths, solo);
    await openStudents(page, solo);
    await toggle(page, solo).click();

    await row(page, solo).getByRole('button', { name: `Revoke the invitation to ${maths.name}` }).click();
    // Revoking asks first: the dialog's confirming button goes ahead.
    await page.getByRole('dialog', { name: 'Revoke this invitation?' }).getByRole('button', { name: 'Revoke invitation' }).click();

    await expect(toggle(page, solo)).toContainText('1 Revoked');
    await expect(toggle(page, solo)).toHaveAttribute('aria-expanded', 'true');
    await expect(row(page, solo).locator('.invite-row')).toContainText(maths.name);
  });

  test('corrects the address of an invitation nobody has registered with, and the invitation follows it', async ({ page }) => {
    const wrong = `typo-${run}@e2e.test`;
    const right = `right-${run}@e2e.test`;
    await api.invite(physics, wrong);
    await openStudents(page, wrong);
    await toggle(page, wrong).click();

    await row(page, wrong).getByLabel('New address').fill(right);
    await row(page, wrong).getByRole('button', { name: 'Change address' }).click();

    await expect(page.getByRole('status').filter({ hasText: `Moved 1 invitation from ${wrong} to ${right}.` })).toBeVisible();
    // The page reads the list again; the search still names the old address, so look for the new one.
    await page.getByLabel('Search students').fill(right);
    await expect(row(page, right)).toHaveCount(1);
    await expect(row(page, wrong)).toHaveCount(0);
  });

  test('corrects a registered student\'s name without signing them out', async ({ page }) => {
    const student = await api.registerCandidate();
    await openStudents(page, student.email);
    await editIcon(page, student.email).click();

    const profile = editPopup(page);
    await profile.getByLabel('Name').fill(`Renamed ${run}`);
    await profile.getByRole('button', { name: 'Save changes' }).click();

    await expect(profile.getByRole('status')).toContainText('Saved: name.');
    await profile.getByRole('button', { name: 'Close' }).click();
    await expect(toggle(page, `Renamed ${run}`)).toBeVisible();
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
  });

  test("changes a registered student's mobile number only after being told it signs them out, and it does", async ({ page }) => {
    const student = await api.registerCandidate();
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
    await openStudents(page, student.email);
    await editIcon(page, student.email).click();

    const profile = editPopup(page);
    await profile.getByLabel('Mobile number').fill(`+9198${Math.floor(10_000_000 + Math.random() * 89_999_999)}`);
    await profile.getByRole('button', { name: 'Save changes' }).click();

    // Nothing is saved until the second click, and the student is still signed in.
    await expect(profile.getByText('Before you save')).toBeVisible();
    await expect(profile.getByText('signed out and signs in again with a code sent to the new number')).toBeVisible();
    expect(await api.isSignedIn(student.accessToken)).toBe(true);

    await profile.getByRole('button', { name: 'Confirm and save' }).click();

    await expect(profile.getByRole('status')).toContainText('Saved: mobile number.');
    await expect(profile.getByRole('status')).toContainText('The student was signed out.');
    expect(await api.isSignedIn(student.accessToken)).toBe(false);
  });

  test("changes a registered student's email address, carries their invitation with it, and signs them out", async ({ page }) => {
    const student = await api.registerCandidate();
    await api.invite(maths, student.email);
    const moved = `moved-${run}-${randomUUID().slice(0, 6)}@e2e.test`;
    await openStudents(page, student.email);
    await editIcon(page, student.email).click();

    // The popup is the same one throughout, even though the row behind it is read again once the address has changed.
    const profile = editPopup(page);
    await profile.getByLabel('Email address').fill(moved);
    await profile.getByRole('button', { name: 'Save changes' }).click();
    await expect(profile.getByText('Their invitations move to it.')).toBeVisible();
    await profile.getByRole('button', { name: 'Confirm and save' }).click();

    await expect(profile.getByRole('status')).toContainText('1 invitation moved to the new email address.');
    expect(await api.isSignedIn(student.accessToken)).toBe(false);
    // The page behind a modal popup cannot be used, so it is closed before the search is.
    await profile.getByRole('button', { name: 'Close' }).click();

    // The same student, found by the new address, holding the invitation that was sent to the old one.
    await page.getByLabel('Search students').fill(moved);
    await expect(row(page, moved)).toHaveCount(1);
    await expect(toggle(page, moved)).toContainText('1 Pending');
  });

  test('refuses an address another student already holds, and changes nothing', async ({ page }) => {
    const student = await api.registerCandidate();
    const other = await api.registerCandidate();
    await openStudents(page, student.email);
    await editIcon(page, student.email).click();

    const profile = editPopup(page);
    await profile.getByLabel('Email address').fill(other.email);
    await profile.getByRole('button', { name: 'Save changes' }).click();
    await profile.getByRole('button', { name: 'Confirm and save' }).click();

    await expect(profile.getByRole('alert')).toContainText('Another account already uses that email address.');
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
  });

  test('deactivates a registered student once asked, which signs them out and keeps the account', async ({ page }) => {
    const student = await api.registerCandidate();
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
    await openStudents(page, student.email);

    await deactivateIcon(page, student.email).click();
    const confirm = page.getByRole('dialog', { name: /^Deactivate / });
    await expect(confirm).toContainText('will be signed out');
    // Asked, not done: the student is still signed in until the staff member confirms.
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
    await confirm.getByRole('button', { name: 'Deactivate', exact: true }).click();

    await expect(page.getByRole('status').filter({ hasText: 'They are signed out and can no longer sign in' })).toBeVisible();
    expect(await api.isSignedIn(student.accessToken)).toBe(false);
    // The account is still on the page, marked, with its profile open to correction and nothing left to deactivate.
    await expect(toggle(page, student.email)).toContainText('Deactivated');
    await expect(editIcon(page, student.email)).toHaveCount(1);
    await expect(deactivateIcon(page, student.email)).toHaveCount(0);
  });

  test('turns a deactivated student back on, without bringing their old session back', async ({ page }) => {
    const student = await api.registerCandidate();
    await openStudents(page, student.email);
    await deactivateIcon(page, student.email).click();
    await page.getByRole('dialog', { name: /^Deactivate / }).getByRole('button', { name: 'Deactivate', exact: true }).click();
    await expect(toggle(page, student.email)).toContainText('Deactivated');
    await expect(reactivateIcon(page, student.email)).toHaveCount(1);

    await reactivateIcon(page, student.email).click();

    await expect(page.getByRole('status').filter({ hasText: 'They can sign in again with a code' })).toBeVisible();
    await expect(toggle(page, student.email)).not.toContainText('Deactivated');
    await expect(reactivateIcon(page, student.email)).toHaveCount(0);
    await expect(deactivateIcon(page, student.email)).toHaveCount(1);
    // What the deactivation ended stays ended: the student has to sign in again.
    expect(await api.isSignedIn(student.accessToken)).toBe(false);
  });

  test('does nothing when the staff member cancels the deactivation', async ({ page }) => {
    const student = await api.registerCandidate();
    await openStudents(page, student.email);

    await deactivateIcon(page, student.email).click();
    await page.getByRole('dialog', { name: /^Deactivate / }).getByRole('button', { name: 'Cancel' }).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(await api.isSignedIn(student.accessToken)).toBe(true);
    await expect(toggle(page, student.email)).not.toContainText('Deactivated');
  });

  test('fits a phone: no sideways scroll, and the row, its icons and the open popup are within the screen and targets of at least 44px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    const student = await api.registerCandidate();
    await openStudents(page, student.email);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);

    // 43.5: Firefox reports a 44px control as 43.99997.
    const height = (await toggle(page, student.email).boundingBox())?.height ?? 0;
    expect(height).toBeGreaterThanOrEqual(43.5);
    // A phone has no tooltip to read, so the actions are not icons at the end of the line: they are in the open row, with their words.
    await expect(editIcon(page, student.email)).toHaveCount(0);
    await toggle(page, student.email).click();
    await expect(editIcon(page, student.email)).toContainText('Edit');
    await expect(deactivateIcon(page, student.email)).toContainText('Deactivate');
    for (const icon of [editIcon(page, student.email), deactivateIcon(page, student.email)]) {
      const box = await icon.boundingBox();
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(43.5);
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(43.5);
    }

    await editIcon(page, student.email).click();
    const popup = editPopup(page);
    await expect(popup.getByLabel('Mobile number')).toBeVisible();
    const popupBox = await popup.boundingBox();
    expect((popupBox?.x ?? -1) + (popupBox?.width ?? 0)).toBeLessThanOrEqual(360);
    expect(popupBox?.x ?? -1).toBeGreaterThanOrEqual(0);
    const save = (await popup.getByRole('button', { name: 'Save changes' }).boundingBox())?.height ?? 0;
    expect(save).toBeGreaterThanOrEqual(43.5);
  });
});
