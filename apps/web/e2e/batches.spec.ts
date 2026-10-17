import { expect, test } from './support/fixtures';
import { staffAccessToken } from './support/fake-api';

// The Batches page, which staff open to see their batches. The page once stayed on "Loading batches..." for good: the app runs without
// zone.js, so a field set when the answer arrives drew nothing, and only a signal redraws the page. A slow answer hid it, because the
// waiting indicator changed a signal as it went away; the fake API answers at once, as a nearby server does, so these tests see it.

const batches = [
  { id: 'b1', examId: 'e1', name: 'Morning batch', status: 'Active', maxMembers: 50, activeMemberCount: 3, createdBy: 'staff-1', createdAt: '2026-10-01T09:00:00Z', updatedAt: '2026-10-01T09:00:00Z' },
  { id: 'b2', examId: 'e1', name: 'Evening batch', status: 'Draft', maxMembers: 30, activeMemberCount: 12, createdBy: 'staff-1', createdAt: '2026-10-02T09:00:00Z', updatedAt: '2026-10-02T09:00:00Z' },
];

test.beforeEach(async ({ page, api }) => {
  void api;
  await page.addInitScript(([key, value]) => localStorage.setItem(key, value), ['exam-platform.auth-token', staffAccessToken('staff-1', 'session-1', ['batch.manage'])] as [string, string]);
});

test.describe('the Batches page', () => {
  test('lists the batches once they are read, and stops saying it is loading', async ({ page }) => {
    await page.route('**/api/v1/batches', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(batches) }));

    await page.goto('/batches');

    await expect(page.getByRole('cell', { name: 'Morning batch' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Evening batch' })).toBeVisible();
    await expect(page.getByRole('row', { name: /Evening batch/ })).toContainText('12 / 30');
    await expect(page.getByText('Loading batches...')).toHaveCount(0);
  });

  test('says there are no batches, with a way to create one, when there are none', async ({ page }) => {
    await page.route('**/api/v1/batches', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));

    await page.goto('/batches');

    await expect(page.getByText('No batches found.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Create one now' })).toBeVisible();
    await expect(page.getByText('Loading batches...')).toHaveCount(0);
  });

  test('says why when the batches cannot be read, and stops saying it is loading', async ({ page }) => {
    await page.route('**/api/v1/batches', (route) => route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ title: 'forbidden' }) }));

    await page.goto('/batches');

    await expect(page.getByText('Failed to load batches')).toBeVisible();
    await expect(page.getByText('Loading batches...')).toHaveCount(0);
  });
});
