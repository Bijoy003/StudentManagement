import { test, expect, AdminEmail, AdminPassword, login, unique } from './fixtures';

test.describe('Auth', () => {
  test('Redirects unauthenticated users to login page', async ({ page }) => {
    await page.goto('/Student');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('Logs in with valid admin credentials', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.locator('#Email').fill(AdminEmail);
    await page.locator('#Password').fill(AdminPassword);
    await page.getByRole('button', { name: 'Login', exact: true }).click();

    // Home/Index renders at the root URL (/).
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByText(`Hello, ${AdminEmail}`)).toBeVisible();
  });

  test('Shows error for invalid credentials and stays on login', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.locator('#Email').fill(`${unique('nobody')}@example.com`);
    await page.locator('#Password').fill('WrongPassword!');
    await page.getByRole('button', { name: 'Login', exact: true }).click();

    await expect(page).toHaveURL(/\/Account\/Login/);
    await expect(page.locator('#Email')).toBeVisible();
    await expect(page.getByText('Hello,')).toHaveCount(0);
  });

  test('Logs out back to login page', async ({ page }) => {
    await login(page);
    await page.getByRole('button', { name: 'Logout' }).click();
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('Home page redirects to login when not authenticated', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('Home page shows welcome when authenticated', async ({ page }) => {
    await login(page);
    await expect(
      page.getByRole('heading', { name: 'Welcome to Student Management' }),
    ).toBeVisible();
    await expect(page.getByText('Manage Students, Courses, and Enrollments easily.')).toBeVisible();
  });

  test('Forgot password with unknown email shows confirmation page', async ({ page }) => {
    await page.goto('/Account/ForgotPassword');
    await page.locator('#Email').fill(`${unique('ghost')}@example.com`);
    await page.getByRole('button', { name: 'Send Reset Link', exact: true }).click();

    await expect(page).toHaveURL(/\/Account\/ForgotPasswordConfirmation/);
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
  });

  test('Access denied page renders', async ({ page }) => {
    await page.goto('/Account/AccessDenied');
    await expect(page.getByRole('heading', { name: 'Access Denied' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Login Again' })).toBeVisible();
  });
});
