import { defineConfig } from 'playwright/test';

const url = 'http://127.0.0.1:9989';

export default defineConfig({
  testDir: './tests/ui',
  reporter: 'list',
  use: {
    baseURL: url,
    viewport: { width: 1200, height: 850 },
    trace: 'retain-on-failure',
    launchOptions: {
      executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined,
    },
  },
  webServer: {
    command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 9989 --strictPort',
    url,
  },
});
