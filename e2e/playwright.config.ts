import { defineConfig, devices } from '@playwright/test';

const browser = process.env.BROWSER ?? 'chromium';
const device =
  browser === 'firefox' ? 'Desktop Firefox' : browser === 'webkit' ? 'Desktop Safari' : 'Desktop Chrome';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: { timeout: 30_000 },
  retries: 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5255',
    headless: process.env.HEADED ? false : true,
    actionTimeout: 30_000,
    navigationTimeout: 30_000,
    trace: 'retain-on-failure',
    ...devices[device],
  },
  projects: [{ name: browser }],
});
