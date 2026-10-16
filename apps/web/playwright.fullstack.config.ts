import { buildConfig } from './playwright.shared';

// The run against the real API and a real database (`npm run e2e:fullstack`): see playwright.shared.ts.
export default buildConfig(true);
