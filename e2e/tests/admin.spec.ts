import { test, expect, login, logout, loginAs, createUserViaAdmin, CreatedUserPassword, unique } from './fixtures';

test.describe('Admin', () => {
  test('Create user as admin succeeds', async ({ page }) => {
    await login(page);
    await page.goto('/Admin/CreateUser');

    await page.locator('#FullName').fill(unique('New User'));
    await page.locator('#Email').fill(`${unique('adminuser')}@example.com`);
    await page.locator('#Password').fill(CreatedUserPassword);
    await page.locator('#ConfirmPassword').fill(CreatedUserPassword);
    await page.locator('#Role').selectOption('User');
    await page.getByRole('button', { name: 'Create User', exact: true }).click();

    await expect(page.getByText('User created successfully!')).toBeVisible();
  });

  test('Create user non-admin is denied', async ({ page }) => {
    const email = await createUserViaAdmin(page);
    await logout(page);
    await loginAs(page, email, CreatedUserPassword);

    await page.goto('/Admin/CreateUser');
    await expect(page).toHaveURL(/\/Account\/AccessDenied/);
    await expect(page.getByRole('heading', { name: 'Access Denied' })).toBeVisible();
  });
});
