import { defineConfig, devices } from '@playwright/test'

/// The browser-borne suite, which exists for the things jsdom is too kind to reproduce.
///
/// The unit suite renders between one `fireEvent` and the next, so two clicks it raises are never
/// in the same batch. A browser does not work that way, and a defect that only appears when React
/// batches two updates together passed that suite for two releases before anyone clicked it.
/// These tests therefore drive a real browser over the built bundle.
///
/// The API is answered from the test rather than by the Host: what is under test here is what the
/// browser does with an interaction, not what the server replies, and pinning the answers keeps
/// the run quick and free of a database.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  // In CI: annotations on the failing lines, and an HTML report kept as an artefact — a failure
  // there is read after the fact, by someone who cannot re-run it locally to see what happened.
  reporter: process.env.CI
    ? [['github'], ['html', { open: 'never' }]]
    : [['list']],

  use: {
    baseURL: 'http://127.0.0.1:4173',
    trace: 'retain-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  // Serves the built bundle out of the Host's wwwroot, so what is exercised is what ships rather
  // than what a development server assembles. `npm run build` has to have run first.
  webServer: {
    command: 'npm run preview -- --port 4173 --strictPort',
    url: 'http://127.0.0.1:4173',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
})
