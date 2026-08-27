import { test, expect, login, createStudent, selectStudentRow, unique } from './fixtures';

test.describe('Student', () => {
  test('Creates student via form and sees it in grid', async ({ page }) => {
    const name = unique('Jane');
    const email = `${unique('jane')}@example.com`;
    const phone = '555-0100';
    const address = '1 Test Street';

    await login(page);
    await page.goto('/Student/Create');
    await page.locator('#name').fill(name);
    await page.locator('#email').fill(email);
    await page.locator('#phone').fill(phone);
    await page.locator('#address').fill(address);
    await page.locator('#btn-save').click();

    // Save navigates back to the index where the grid loads via AJAX.
    await expect(page).toHaveURL(/\/Student\/Index/);
    await expect(page.locator('#student-grid')).toContainText(email, { timeout: 10_000 });
    await expect(page.locator('#student-grid')).toContainText(name);
  });

  test('Rejects empty form with client-side validation', async ({ page }) => {
    await login(page);
    await page.goto('/Student/Create');
    await page.locator('#btn-save').click();

    // Client-side validation shows a toastr warning and stays on the create page.
    await expect(page.locator('#toast-container')).toContainText("Name can't be empty", {
      timeout: 5_000,
    });
    await expect(page).toHaveURL(/\/Student\/Create/);
  });

  test('Edits student via grid selection', async ({ page }) => {
    const name = unique('EditMe');
    const email = `${unique('editme')}@example.com`;

    await login(page);
    await createStudent(page, name, email);

    await selectStudentRow(page, email);
    await page.getByRole('button', { name: 'Edit', exact: true }).click();

    await expect(page).toHaveURL(/\/Student\/Create/);
    await expect(page.locator('#name')).toHaveValue(name);
    await expect(page.locator('#email')).toHaveValue(email);

    const updatedName = `${name} Updated`;
    await page.locator('#name').fill(updatedName);
    await page.locator('#btn-save').click();

    await expect(page).toHaveURL(/\/Student\/Index/);
    await expect(page.locator('#student-grid')).toContainText(updatedName, { timeout: 10_000 });
  });

  test('Deletes student via grid selection', async ({ page }) => {
    const name = unique('DeleteMe');
    const email = `${unique('deleteme')}@example.com`;

    await login(page);
    await createStudent(page, name, email);

    await selectStudentRow(page, email);
    await page.getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(page.locator('#student-grid').getByText(email, { exact: true })).toHaveCount(0, {
      timeout: 10_000,
    });
  });

  test('Edit without selection shows toastr warning', async ({ page }) => {
    await login(page);
    await page.goto('/Student/Index');
    await page.getByRole('button', { name: 'Edit', exact: true }).click();

    await expect(page.locator('#toast-container')).toContainText('Plese select a student.', {
      timeout: 5_000,
    });
  });

  test('Enrolled in more than filters by course count', async ({ page }) => {
    await login(page);
    await page.goto('/Student/EnrolledInMoreThan');

    await expect(
      page.getByRole('heading', { name: 'Students Enrolled in More than 2 Courses' }),
    ).toBeVisible();

    await page.locator('#courseCount').fill('1');
    await page.getByRole('button', { name: 'Filter', exact: true }).click();

    await expect(page).toHaveURL(/courseCount=1/);
    await expect(
      page.getByRole('heading', { name: 'Students Enrolled in More than 1 Courses' }),
    ).toBeVisible();

    await page.locator('#courseCount').fill('9999');
    await page.getByRole('button', { name: 'Filter', exact: true }).click();

    await expect(page.getByText('No students found for this filter.')).toBeVisible();
  });
});
