import { test, expect, login, logout, loginAs, createUserViaAdmin, CreatedUserPassword, unique } from './fixtures';

test.describe('Account', () => {
  test('Profile page shows user info', async ({ page }) => {
    await login(page);
    await page.goto('/Account/Profile');

    await expect(page.getByRole('heading', { name: 'My Profile' })).toBeVisible();
    await expect(page.locator("input[value='admin@admin.com']")).toBeVisible();
    await expect(page.locator("input[value='Admin']")).toBeVisible();
  });

  test('Profile updates full name', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Account/Profile');
    const newName = unique('Renamed');
    await page.locator('#FullName').fill(newName);
    await page.getByRole('button', { name: 'Save Changes', exact: true }).click();

    await expect(page.getByText('Profile updated successfully.')).toBeVisible();
    await expect(page.locator('#FullName')).toHaveValue(newName);
  });

  test('Change password wrong current password stays on page', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Account/ChangePassword');
    await page.locator('#CurrentPassword').fill('WrongCurrent1');
    await page.locator('#NewPassword').fill('NewPass123');
    await page.locator('#ConfirmPassword').fill('NewPass123');
    await page.getByRole('button', { name: 'Update Password', exact: true }).click();

    await expect(page).toHaveURL(/\/Account\/ChangePassword/);
    await expect(page.getByText('Password changed successfully.')).toHaveCount(0);
  });

  test('Change password valid flow relogin with new password', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Account/ChangePassword');
    await page.locator('#CurrentPassword').fill(CreatedUserPassword);
    await page.locator('#NewPassword').fill('NewPass123');
    await page.locator('#ConfirmPassword').fill('NewPass123');
    await page.getByRole('button', { name: 'Update Password', exact: true }).click();

    await expect(page).toHaveURL(/\/Account\/Profile/);

    await logout(page);
    await loginAs(page, email, 'NewPass123');
  });

  test('Two factor enable shows setup', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Account/Profile');
    await page.getByRole('button', { name: 'Enable 2FA', exact: true }).click();

    await expect(page.getByRole('heading', { name: 'Set up an authenticator app' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Verify & Enable 2FA' })).toBeVisible();
    await expect(page.locator("img[alt='Authenticator QR Code']")).toBeVisible();
  });

  test('Two factor wrong code does not enable', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Account/Profile');
    await page.getByRole('button', { name: 'Enable 2FA', exact: true }).click();

    await page.locator('#MfaCode').fill('000000');
    await page.getByRole('button', { name: 'Verify & Enable 2FA', exact: true }).click();

    // Server re-renders the Profile view (URL stays on ConfirmAuthenticator); 2FA must stay disabled.
    await expect(page.getByText('Disabled', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Enable 2FA', exact: true })).toBeVisible();
  });
});
