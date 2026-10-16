import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env['E2E_PORT'] ?? 4300);
const onCi = !!process.env['CI'];

// A machine that already has a Chromium (the cloud sandbox this repository is developed in) can point the Chromium project at it
// instead of downloading one. CI downloads every engine with `npx playwright install --with-deps`.
const chromiumPath = process.env['E2E_CHROMIUM_PATH'];

/**
 * Browser tests for the production build (NFR-8, D19): the same journeys on Chromium, Firefox and WebKit. WebKit is the engine Safari
 * is built on; it is not Safari, so the real-phone checks in docs/compliance/accessibility-manual-checklist.md stay.
 *
 * The API is stubbed in the browser (e2e/support/fake-api.ts), so no server or database is needed. Build the app first (`npm run build`).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: onCi,
  // No retries: a test that passes on a second try on one engine is a browser difference worth seeing, not hiding.
  retries: 0,
  reporter: onCi ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    // The screens are tested in English; the wording comes from the same message file the app uses (e2e/support/messages.ts).
    locale: 'en-US',
    trace: 'retain-on-failure',
    // Playwright cannot stub the requests a service worker handles, so the journeys run without one; e2e/pwa.spec.ts turns it on.
    serviceWorkers: 'block',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], launchOptions: chromiumPath ? { executablePath: chromiumPath } : {} },
    },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    command: 'node e2e/serve.mjs',
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: !onCi,
    env: { E2E_PORT: String(port) },
  },
});
