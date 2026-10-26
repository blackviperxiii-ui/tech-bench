import { expect, test } from "@playwright/test";

// Each test gets a fresh browser context, so IndexedDB starts empty.

test("search finds a shipped fault code and no passwords", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("status").first()).toContainText(
    "Codes 155  ·  Passwords 0  ·  Manuals 2147  ·  Filters 63  ·  Equipment 37",
  );
  await page.getByLabel("Search the knowledge base").fill("1AVPT");
  const results = page.getByRole("listbox", { name: "Results" });
  await expect(results.getByRole("option")).toHaveCount(1);
  await expect(results.getByRole("option").first()).toHaveText("CODEingersoll-rand  01  —  Sensor Failure 1AVPT", {
    useInnerText: false,
  });
  await expect(page.getByRole("status").first()).toContainText("1 hits");
  await expect(page.locator(".detail").first()).toContainText("Sensor Failure 1AVPT");

  await page.getByLabel("Kind").selectOption("PASSWORD");
  await page.getByLabel("Search the knowledge base").fill("");
  await expect(page.getByRole("status").first()).toContainText("0 hits");
});

test("a work order is saved in the browser and reopens after a reload", async ({ page }) => {
  await page.goto("/");
  const nav = page.getByRole("navigation", { name: "Pages" });
  await nav.getByRole("button", { name: "Work orders" }).click();
  await page.getByRole("button", { name: "New", exact: true }).click();

  const form = page.locator(".wo-form");
  await form.getByLabel("WO number").fill("WEB-1001");
  await form.getByLabel("Customer", { exact: true }).fill("Acme Rental");
  await form.getByLabel("Model").fill("HP450");
  await form.getByLabel("Serial").fill("12345");
  await form.getByLabel("Notes").fill("Checked the DEF line.\nNo leaks.");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.locator(".wo .status")).toHaveText("Saved WO WEB-1001");

  await page.getByRole("button", { name: "Log on" }).click();
  await expect(page.locator(".wo .status")).toHaveText("Shop logon saved.");
  await expect(page).toHaveTitle("Tech Bench 0.1.0  ·  WO WEB-1001  HP450 12345");

  await page.reload();
  await nav.getByRole("button", { name: "Work orders" }).click();
  const list = page.getByRole("listbox", { name: "Work orders" });
  await expect(list.getByRole("option")).toHaveText(["WEB-1001  Acme Rental  HP450 12345  [logged on]"]);
  // The saved job reopens the last work order on startup, as the desktop app does.
  await expect(list.getByRole("option").first()).toHaveAttribute("aria-selected", "true");
  await list.getByRole("option").first().click();
  await expect(form.getByLabel("WO number")).toHaveValue("WEB-1001");
  await expect(form.getByLabel("Customer", { exact: true })).toHaveValue("Acme Rental");
  await expect(form.getByLabel("Notes")).toHaveValue("Checked the DEF line.\nNo leaks.");
  await expect(form.locator("pre.detail").last()).toContainText("shop-logon  [shop]  Shop record only.");
  await expect(page.getByRole("group", { name: "Job" }).getByRole("combobox")).toHaveText(
    /WEB-1001  Acme Rental  HP450 12345  \[logged on\]/,
  );
});

test("the adapter and shop-folder parts are hidden", async ({ page }) => {
  await page.goto("/");
  const nav = page.getByRole("navigation", { name: "Pages" });
  await expect(nav.getByRole("button", { name: "Settings" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Add code to KB" })).toHaveCount(0);
  await expect(page.getByTitle("Shop sync")).toHaveCount(0);

  await nav.getByRole("button", { name: "INLINE 7" }).click();
  await expect(page.locator(".inline7 .notice")).toContainText(
    "Live bus needs the Windows build: the INLINE 7 adapter only has 32-bit Windows RP1210 drivers.",
  );
  await expect(page.getByRole("button", { name: "Connect" })).toHaveCount(0);
  await page.getByLabel("SPN").fill("5246");
  await page.getByLabel("FMI").fill("0");
  await page.getByRole("button", { name: "Explain" }).click();
  await expect(page.locator(".code-help")).toContainText("5246");

  await nav.getByRole("button", { name: "Work orders" }).click();
  await expect(page.getByRole("button", { name: "Share with shop" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Add photo" })).toHaveCount(0);
});
