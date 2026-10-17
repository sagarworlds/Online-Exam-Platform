import { randomUUID } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { RealApi, type PublishedExam } from './real-api';

// The Invitations page gathers every invitation to one student into one group that folds, with the name of each exam. The exam names come
// from the API (the invite module does not own them), and which groups are open depends on what the API says is still pending, so the page
// and the API are tested together; nothing is stubbed.

const run = randomUUID().slice(0, 8);
const AUTH_TOKEN_STORAGE_KEY = 'exam-platform.auth-token';
// Each exam gets a question of its own wording: the bank refuses a second question that repeats one it has.
const questionFor = (subject: string) => ({ text: `Which colour is grass in ${subject}? (${run})`, right: 'Green', wrong: 'Red' });

let api: RealApi;
let maths: PublishedExam;
let physics: PublishedExam;

// Two students, so the page has a group for each: Asha is invited to both exams, Ben to one. The page lists the newest 200 invitations
// of the whole database, so every address here carries this run's id and the page is read through them alone.
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

async function openInvitations(page: Page): Promise<void> {
  const token = await api.administratorToken();
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), [AUTH_TOKEN_STORAGE_KEY, token] as [string, string]);
  await page.goto('/invites');
  await expect(page.getByRole('heading', { name: 'Invitations' })).toBeVisible();
}

const group = (page: Page, email: string): Locator => page.locator('section.invite-group').filter({ hasText: email });
const toggle = (page: Page, email: string): Locator => group(page, email).locator('.invite-group__toggle');

test.describe('the Invitations page', () => {
  test('shows a student invited to two exams once, with both exam names, and a student invited to one beside them', async ({ page }) => {
    await openInvitations(page);

    await expect(group(page, asha)).toHaveCount(1);
    const rows = group(page, asha).locator('.invite-row');
    await expect(rows).toHaveCount(2);
    // Newest first: Physics was invited second.
    await expect(rows.nth(0)).toContainText(physics.name);
    await expect(rows.nth(1)).toContainText(maths.name);
    await expect(group(page, asha).locator('.invite-group__toggle')).toContainText('2 invitations');

    await expect(group(page, ben).locator('.invite-row')).toHaveCount(1);
    await expect(group(page, ben).locator('.invite-row')).toContainText(maths.name);
    // Nothing of Asha's is listed under Ben.
    await expect(group(page, ben)).not.toContainText(physics.name);
  });

  test('has the students with something pending open, folds one away and opens it again', async ({ page }) => {
    await openInvitations(page);
    const header = toggle(page, asha);

    await expect(header).toHaveAttribute('aria-expanded', 'true');
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(2);

    await header.click();
    await expect(header).toHaveAttribute('aria-expanded', 'false');
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(0);
    // Folded, the heading still says who it is and what is in it.
    await expect(header).toContainText(asha);
    await expect(header).toContainText('2 Pending');

    await header.click();
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(2);
  });

  test('Collapse all folds every student and Expand all opens them again', async ({ page }) => {
    await openInvitations(page);

    await page.getByRole('button', { name: 'Collapse all' }).click();
    await expect(page.locator('.invite-row')).toHaveCount(0);

    await page.getByRole('button', { name: 'Expand all' }).click();
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(2);
    await expect(group(page, ben).locator('.invite-row')).toHaveCount(1);
  });

  test('keeps a student open after one of their invitations is revoked, and shows it revoked', async ({ page }) => {
    await openInvitations(page);

    await group(page, ben).getByRole('button', { name: `Revoke the invitation to ${maths.name}` }).click();

    // Ben had one invitation and it is settled now, which is the case that would fold his group up if the page reset what is open.
    await expect(toggle(page, ben)).toContainText('1 Revoked');
    await expect(toggle(page, ben)).toHaveAttribute('aria-expanded', 'true');
    await expect(group(page, ben).locator('.invite-row')).toContainText(maths.name);
    // Asha's invitations are untouched.
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(2);
  });

  test('fits a phone: no sideways scroll, and each heading is a target of at least 44px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await openInvitations(page);
    await expect(group(page, asha).locator('.invite-row')).toHaveCount(2);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);

    const height = (await toggle(page, asha).boundingBox())?.height ?? 0;
    expect(height).toBeGreaterThanOrEqual(44);
  });
});
