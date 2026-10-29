import { defineConfig, devices } from "@playwright/test";

// The web build (`npm run build:web`) served by `vite preview`, on all three browser engines.
export default defineConfig({
  testDir: "app/e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: { baseURL: "http://localhost:4173", trace: "retain-on-failure" },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "firefox", use: { ...devices["Desktop Firefox"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
  ],
  webServer: {
    command: "npm run preview:web",
    url: "http://localhost:4173",
    reuseExistingServer: !process.env.CI,
  },
});
