import { expect, test as base } from '@playwright/test';
import { FakeApi } from './fake-api';

/**
 * `test` with a fake API already answering the page's `/api` requests. After each test it checks the app asked for nothing the fake
 * API does not know, so a new call from the app fails a test here instead of passing unnoticed.
 */
export const test = base.extend<{ api: FakeApi }>({
  api: async ({ page }, use) => {
    const api = new FakeApi();
    await api.install(page);
    await use(api);
    expect(api.unexpected, 'the app called an endpoint the fake API does not answer').toEqual([]);
  },
});

export { expect };
