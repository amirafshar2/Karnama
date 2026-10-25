import { defineConfig, devices } from "@playwright/test";

/**
 * Visual CRUD test (docs/SKILLS.md §6): every step leaves a screenshot in screenshots/{device}-{nn}-{name}.png.
 * Three devices: desktop 1366, Pixel 7, and a cheap 360px phone.
 * DEMO_URL / EMPTY_URL point at the two installs started by run-visual.sh.
 */
export default defineConfig({
  testDir: "./specs",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { outputFolder: "report", open: "never" }]],
  use: {
    locale: "fa-IR",
    timezoneId: "Asia/Tehran",
    trace: "retain-on-failure",
    actionTimeout: 15_000,
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"], viewport: { width: 1366, height: 768 } } },
    { name: "pixel7", use: { ...devices["Pixel 7"] } },
    { name: "phone360", use: { ...devices["Galaxy S8"], viewport: { width: 360, height: 740 } } },
  ],
});
