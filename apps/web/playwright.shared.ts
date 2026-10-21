import { defineConfig, devices, type PlaywrightTestConfig } from '@playwright/test';

const onCi = !!process.env['CI'];

// A machine that already has a Chromium (the cloud sandbox this repository is developed in) can point the Chromium project at it
// instead of downloading one. CI downloads every engine with `npx playwright install --with-deps`.
const chromiumPath = process.env['E2E_CHROMIUM_PATH'];
const chromium = { ...devices['Desktop Chrome'], launchOptions: chromiumPath ? { executablePath: chromiumPath } : {} };
const firefox = devices['Desktop Firefox'];
// WebKit is the engine Safari is built on. It is not Safari, so the real-phone checks in
// docs/compliance/accessibility-manual-checklist.md stay.
const webkit = devices['Desktop Safari'];

// A phone's or a tablet's size, touch and way of holding, on the Chromium that is at hand. The layouts are chosen by the room there is, which
// every engine measures the same way, so one engine is enough here; the three desktop projects below run the same journeys on all three.
const onDevice = (name: string) => ({ ...devices[name], defaultBrowserType: 'chromium' as const, launchOptions: chromium.launchOptions });

const stubbedPort = Number(process.env['E2E_PORT'] ?? 4300);
const fullStackPort = 4301;
const apiUrl = 'http://127.0.0.1:5080';

/** The run where the API is replaced inside the browser: no server or database, and it is what shows each engine runs the screens. */
const stubbed: Partial<PlaywrightTestConfig> = {
  fullyParallel: true,
  projects: [
    { name: 'chromium', testIgnore: '**/fullstack/**', use: chromium },
    { name: 'firefox', testIgnore: '**/fullstack/**', use: firefox },
    { name: 'webkit', testIgnore: '**/fullstack/**', use: webkit },
    // The pages a candidate uses, at the size and in the way of holding of a phone, a small phone, a phone on its side and a tablet.
    { name: 'phone', testMatch: '**/layout.spec.ts', use: onDevice('Pixel 7') },
    { name: 'phone-landscape', testMatch: '**/layout.spec.ts', use: onDevice('Pixel 7 landscape') },
    { name: 'small-phone', testMatch: '**/layout.spec.ts', use: onDevice('Galaxy S9+') },
    { name: 'tablet', testMatch: '**/layout.spec.ts', use: onDevice('iPad (gen 7)') },
    { name: 'tablet-landscape', testMatch: '**/layout.spec.ts', use: onDevice('iPad (gen 7) landscape') },
  ],
  webServer: {
    command: 'node e2e/serve.mjs',
    url: `http://127.0.0.1:${stubbedPort}/`,
    reuseExistingServer: !onCi,
    env: { E2E_PORT: String(stubbedPort) },
  },
};

/** The run where the real API runs against a real Postgres, and it is what shows the page and the API agree. */
const real: Partial<PlaywrightTestConfig> = {
  // One test at a time: a sign-in code is read from the one API log, and the next code written there must be the one just asked for.
  fullyParallel: false,
  workers: 1,
  projects: ([['fullstack-chromium', chromium], ['fullstack-firefox', firefox], ['fullstack-webkit', webkit]] as const).map(([name, device]) => ({
    name,
    testMatch: '**/fullstack/**/*.spec.ts',
    use: { ...device, baseURL: `http://127.0.0.1:${fullStackPort}` },
  })),
  webServer: [
    {
      // The API migrates and seeds its own database on start, so it can take a while on a fresh one.
      command: 'node e2e/fullstack/run-api.mjs',
      url: `${apiUrl}/v1/health`,
      timeout: 180_000,
      reuseExistingServer: false,
    },
    {
      command: 'node e2e/serve.mjs',
      url: `http://127.0.0.1:${fullStackPort}/`,
      reuseExistingServer: false,
      env: { E2E_PORT: String(fullStackPort), E2E_API_URL: apiUrl },
    },
  ],
};

/**
 * The Playwright configuration for browser tests of the production build (NFR-8, D19), on Chromium, Firefox and WebKit.
 *
 * - Stubbed (`npm run e2e`, playwright.config.ts): needs only the built app (`npm run build`).
 * - Full stack (`npm run e2e:fullstack`, playwright.fullstack.config.ts): needs the built app and the built API (e2e/fullstack).
 *
 * @param fullStack Whether to run against the real API.
 */
export function buildConfig(fullStack: boolean) {
  return defineConfig({
    testDir: './e2e',
    forbidOnly: onCi,
    // No retries: a test that passes on a second try on one engine is a browser difference worth seeing, not hiding.
    retries: 0,
    reporter: onCi ? [['github'], ['html', { open: 'never' }]] : [['list']],
    use: {
      baseURL: `http://127.0.0.1:${stubbedPort}`,
      // The screens are tested in English; the wording comes from the same message file the app uses (e2e/support/messages.ts).
      locale: 'en-US',
      trace: 'retain-on-failure',
      // Playwright cannot stub the requests a service worker handles, so the journeys run without one; e2e/pwa.spec.ts turns it on.
      serviceWorkers: 'block',
    },
    ...(fullStack ? real : stubbed),
  });
}
