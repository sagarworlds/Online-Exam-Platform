import { expect, test, type Page } from '@playwright/test';
import { text } from './support/messages';

// Here the service worker is on, as for a real visitor. The API is not stubbed: with a worker in the way Playwright cannot see the
// requests it handles, and none of these tests needs an answer from the API. The static server reports the API as unreachable.
test.use({ serviceWorkers: 'allow' });

/**
 * Resolves once a service worker is active for the page, and then reloads so that it controls the page. A browser build with no
 * service worker support at all (some automation builds of WebKit) skips the test with that reason, rather than failing for a gap in
 * the test browser: the app works without a worker, and a real Safari has one.
 */
async function waitForWorkerToControl(page: Page): Promise<void> {
  await page.goto('/login');
  const supported = await page.evaluate(() => 'serviceWorker' in navigator);
  test.skip(!supported, 'This browser build has no service worker support.');

  // The app registers the worker when it is stable, or after 30 seconds at the latest.
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await page.reload();
  await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true);
}

test.describe('installable app', () => {
  test('the page links a manifest that names the app and whose icons exist', async ({ page, request }) => {
    await page.goto('/login');

    const href = await page.locator('link[rel="manifest"]').getAttribute('href');
    expect(href).not.toBeNull();
    const manifestUrl = new URL(href!, page.url()).toString();
    const manifest = (await (await request.get(manifestUrl)).json()) as {
      name: string;
      short_name: string;
      display: string;
      icons: { src: string; sizes: string }[];
    };

    expect(manifest.name).not.toBe('');
    expect(manifest.short_name).not.toBe('');
    expect(manifest.display).toBe('standalone');
    expect(manifest.icons.length).toBeGreaterThan(0);
    for (const icon of manifest.icons) {
      const response = await request.get(new URL(icon.src, manifestUrl).toString());
      expect(response.ok(), `${icon.src} (${icon.sizes}) is served`).toBe(true);
    }
  });

  test('a service worker takes control of the page after the first visit', async ({ page }) => {
    await waitForWorkerToControl(page);
  });

  test('once the worker is in control the app shell opens with no network', async ({ page, context }) => {
    await waitForWorkerToControl(page);

    await context.setOffline(true);
    await page.reload();

    await expect(page.getByRole('heading', { level: 1, name: text('login.title') })).toBeVisible();
  });
});
