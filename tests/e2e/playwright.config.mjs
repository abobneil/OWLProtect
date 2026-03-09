import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: ".",
  timeout: 60_000,
  use: {
    baseURL: process.env.OWLP_E2E_ADMIN_PORTAL_URL ?? "http://127.0.0.1:4173",
    browserName: "chromium",
    headless: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure"
  },
  reporter: [["list"], ["html", { outputFolder: "artifacts/e2e/playwright-report", open: "never" }]]
});
