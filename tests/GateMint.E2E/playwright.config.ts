import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './specs', timeout: 45_000, retries: 0, fullyParallel: false,
  use: { baseURL: process.env.GATEMINT_BASE_URL ?? 'http://localhost:5080', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
