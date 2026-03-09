import { expect, test } from "@playwright/test";

const adminUsername = process.env.OWLP_E2E_ADMIN_USERNAME ?? "admin";
const adminPassword = process.env.OWLP_E2E_ADMIN_PASSWORD ?? "change-local-bootstrap-admin-password";
const adminNewPassword = process.env.OWLP_E2E_ADMIN_NEW_PASSWORD ?? adminPassword;

test("bootstrap login reaches the fleet workflow", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("Username").fill(adminUsername);
  await page.getByLabel("Password").fill(adminPassword);
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.waitForLoadState("networkidle");

  if (page.url().includes("/bootstrap")) {
    const currentPassword = page.getByLabel("Current password");
    if (await currentPassword.isVisible()) {
      await currentPassword.fill(adminPassword);
      await page.getByLabel("New password").fill(adminNewPassword);
      await page.getByRole("button", { name: "Rotate password" }).click();
    }

    const enrollButton = page.getByRole("button", { name: /Enroll MFA|MFA already enrolled/i });
    if (await enrollButton.isVisible()) {
      await enrollButton.click();
    }

    await page.goto("/dashboard");
  }

  await expect(page.getByText("Fleet Health")).toBeVisible();
  await page.goto("/fleet");
  await expect(page.getByText("Enrollment and live operator actions")).toBeVisible();
  await expect(page.getByText("Fault-domain classification")).toBeVisible();
});
