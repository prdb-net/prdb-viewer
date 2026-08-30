import { defineConfig, devices } from '@playwright/test'

import { BASE_URL } from './e2e-full/installation'

/// The browser suite that talks to a real installation, rather than to answers the test wrote.
///
/// `playwright.config.ts` pins every `/api/**` reply, which is right for what it asks — what a
/// click does to the address — and means the server is never involved. Nothing there can notice a
/// screen reading a field the API does not send, or a filter the API ignores. This one runs the
/// published image over a seeded library and a stand-in prdb, so the contract between the two
/// halves is what is under test.
///
/// It is deliberately not in CI: it builds an image and runs six lanes twice, which is too much to
/// ask of every push. Run it before a release, with `npm run test:e2e:full`.
export default defineConfig({
  testDir: './e2e-full',
  // One installation is shared by every test, so they must not race each other through it.
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [['list']],

  globalSetup: './e2e-full/global-setup.ts',
  globalTeardown: './e2e-full/global-teardown.ts',

  // Building the image and draining the lanes takes minutes on a first run.
  timeout: 60_000,
  globalTimeout: 30 * 60_000,

  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
