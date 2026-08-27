import { test, expect, login, createCourse, unique } from './fixtures';

test.describe('Course', () => {
  test('Creates course via form and sees it in list', async ({ page }) => {
    const name = unique('Math');

    await login(page);
    await page.goto('/Course/Create');
    await page.locator('#name').fill(name);
    await page.locator('#credits').fill('4');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page).toHaveURL(/\/Course\/Index/);
    await expect(page.locator('table')).toContainText(name);
  });

  test('Student count per course page renders', async ({ page }) => {
    await login(page);
    await page.goto('/Course/StudentCountPerCourse');
    await expect(page.getByRole('heading', { name: 'Student Count per Course' })).toBeVisible();
    await expect(page.locator('table')).toBeVisible();
  });

  test('Empty form stays on create page', async ({ page }) => {
    await login(page);
    await page.goto('/Course/Create');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    // Server-side validation rejects the empty payload (400); course.js has no error
    // handler, so the page must not navigate away.
    await expect(page).toHaveURL(/\/Course\/Create/);
    await expect(page.locator('#name')).toBeVisible();
  });

  test('Edit course form is prefilled', async ({ page }) => {
    const name = unique('CourseEdit');

    await login(page);
    await createCourse(page, name);

    const row = page.locator('table tr', { hasText: name });
    const id = (await row.locator('td').first().innerText()).trim();

    await page.goto(`/Course/Create?id=${id}`);
    await expect(page.locator('#name')).toHaveValue(name);
    await expect(page.locator('#credits')).toHaveValue('3');
  });
});
