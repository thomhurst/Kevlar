import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  use: {
    baseURL: 'http://127.0.0.1:3000/Kevlar/',
  },
  webServer: [
    {
      command: 'npm run serve -- --host 127.0.0.1 --port 3000',
      url: 'http://127.0.0.1:3000/Kevlar/',
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: 'node tests/static-server.mjs',
      url: 'http://127.0.0.1:3001/index.html',
      reuseExistingServer: false,
      timeout: 10_000,
    },
  ],
});
