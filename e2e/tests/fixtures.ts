import { expect, test, type Page } from '@playwright/test';

export { expect, test };

export const AdminEmail = 'admin@admin.com';
export const AdminPassword = 'Admin123';
export const CreatedUserPassword = 'Test1234';

export const BaseUrl = process.env.E2E_BASE_URL ?? 'http://localhost:5255';

/** Returns a unique suffix for names/emails to avoid collisions across runs. */
export function unique(label: string): string {
  return `${label}_${Date.now()}_${Math.floor(Math.random() * 1000)}`;
}

/** Logs in as the seeded admin user via the UI. */
export function login(page: Page): Promise<void> {
  return loginAs(page, AdminEmail, AdminPassword);
}

/** Logs in as the given user via the UI and waits for the authenticated navbar. */
export async function loginAs(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await page.getByRole('button', { name: 'Login', exact: true }).click();
  // Home/Index renders at the root URL (/); wait for the authenticated navbar.
  await page.getByText(`Hello, ${email}`).waitFor();
}

/** Logs the current user out via the navbar and waits for the login page. */
export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Logout' }).click();
  await expect(page).toHaveURL(/\/Account\/Login/);
}

/**
 * Creates a user (password {@link CreatedUserPassword}) through the Admin UI
 * and leaves the session logged in as admin. Returns the created user's email.
 */
export async function createUserViaAdmin(page: Page, role = 'User'): Promise<string> {
  const fullName = unique('E2E User');
  const email = `${unique('user')}@example.com`;

  await login(page);
  await page.goto('/Admin/CreateUser');
  await page.locator('#FullName').fill(fullName);
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(CreatedUserPassword);
  await page.locator('#ConfirmPassword').fill(CreatedUserPassword);
  await page.locator('#Role').selectOption(role);
  await page.getByRole('button', { name: 'Create User', exact: true }).click();

  await expect(page.getByText('User created successfully!')).toBeVisible();
  return email;
}

/** Creates a student via the UI. Requires an authenticated session. */
export async function createStudent(page: Page, name: string, email: string): Promise<void> {
  await page.goto('/Student/Create');
  await page.locator('#name').fill(name);
  await page.locator('#email').fill(email);
  await page.locator('#phone').fill('555-0100');
  await page.locator('#address').fill('1 Test Street');
  await page.locator('#btn-save').click();

  await expect(page).toHaveURL(/\/Student\/Index/);
  await expect(page.locator('#student-grid')).toContainText(email, { timeout: 10_000 });
}

/** Creates a course via the UI. Requires an authenticated session. */
export async function createCourse(page: Page, name: string, credits = '3'): Promise<void> {
  await page.goto('/Course/Create');
  await page.locator('#name').fill(name);
  await page.locator('#credits').fill(credits);
  await page.getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page).toHaveURL(/\/Course\/Index/);
  await expect(page.locator('table')).toContainText(name);
}

/** Selects the grid row containing the given text (DataTable row-click selection). */
export async function selectStudentRow(page: Page, matchText: string): Promise<void> {
  await page.locator('#student-grid').getByText(matchText, { exact: true }).click();
}
