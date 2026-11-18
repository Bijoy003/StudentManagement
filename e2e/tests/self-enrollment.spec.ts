import { test, expect, login, logout, loginAs, createUserViaAdmin, createStudent, CreatedUserPassword, unique } from './fixtures';
import type { Page } from '@playwright/test';

/** Links a user account to a student record through the Admin/Users page. Requires admin session. */
async function linkUserToStudent(page: Page, userEmail: string, studentName: string): Promise<void> {
  await page.goto('/Admin/Users');
  const row = page.locator('tbody tr', { hasText: userEmail });
  const select = row.locator('select[name="StudentId"]');
  const value = await select.locator('option', { hasText: studentName }).first().getAttribute('value');
  await select.selectOption(value!);
  await row.getByRole('button', { name: 'Link', exact: true }).click();
  await expect(page.locator('.alert-success')).toContainText(`Linked ${userEmail}`);
}

test.describe('Student self-enrollment', () => {
  test('Linked student enrolls from the catalog and drops from My Courses', async ({ page }) => {
    const studentName = unique('E2E Student');
    const studentEmail = `${unique('student')}@example.com`;
    const courseName = unique('E2E Course');

    // Admin: prepare a student record, a course, and a linked Student-role account.
    await login(page);
    await createStudent(page, studentName, studentEmail);
    await page.goto('/Course/Create');
    await page.locator('#name').fill(courseName);
    await page.locator('#credits').fill('3');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/Course\/Index/);
    const accountEmail = await createUserViaAdmin(page, 'Student');
    await linkUserToStudent(page, accountEmail, studentName);

    // Student: enroll from the catalog.
    await logout(page);
    await loginAs(page, accountEmail, CreatedUserPassword);
    await page.goto('/Course/Index');
    await page.locator('tr', { hasText: courseName }).getByRole('button', { name: 'Enroll', exact: true }).click();
    await expect(page).toHaveURL(/\/Student\/MyCourses/);
    await expect(page.locator('.alert-success')).toContainText(`Enrolled in ${courseName}.`);
    await expect(page.locator('table')).toContainText(courseName);

    // Catalog now marks the course as enrolled (no duplicate Enroll button).
    await page.goto('/Course/Index');
    const enrolledRow = page.locator('tr', { hasText: courseName });
    await expect(enrolledRow.getByText('Enrolled', { exact: true })).toBeVisible();
    await expect(enrolledRow.getByRole('button', { name: 'Enroll', exact: true })).toHaveCount(0);

    // Student: drop from My Courses. The confirm() dialog opens synchronously during
    // the click, so the handler must be registered BEFORE clicking (awaiting the click
    // first would deadlock against the modal dialog).
    await page.goto('/Student/MyCourses');
    page.once('dialog', dialog => dialog.accept());
    await page.locator('tr', { hasText: courseName }).getByRole('button', { name: 'Drop', exact: true }).click();
    await expect(page.locator('.alert-success')).toContainText('Course dropped.');
    await expect(page.getByText('You are not enrolled in any courses yet.')).toBeVisible();
  });

  test('Admin-set capacity blocks enrollment when the course is full', async ({ page }) => {
    const courseName = unique('E2E Full Course');
    const studentAName = unique('E2E Student A');
    const studentBName = unique('E2E Student B');

    // Admin: course capped at 1 seat, two linked student accounts.
    await login(page);
    await page.goto('/Course/Create');
    await page.locator('#name').fill(courseName);
    await page.locator('#credits').fill('3');
    await page.locator('#max-enrollments').fill('1');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/Course\/Index/);

    await createStudent(page, studentAName, `${unique('a')}@example.com`);
    await createStudent(page, studentBName, `${unique('b')}@example.com`);
    const accountA = await createUserViaAdmin(page, 'Student');
    await linkUserToStudent(page, accountA, studentAName);
    const accountB = await createUserViaAdmin(page, 'Student');
    await linkUserToStudent(page, accountB, studentBName);

    // Student A takes the only seat.
    await logout(page);
    await loginAs(page, accountA, CreatedUserPassword);
    await page.goto('/Course/Index');
    await page.locator('tr', { hasText: courseName }).getByRole('button', { name: 'Enroll', exact: true }).click();
    await expect(page).toHaveURL(/\/Student\/MyCourses/);
    await expect(page.locator('table')).toContainText(courseName);

    // Student B sees the course as Full, with no Enroll button.
    await logout(page);
    await loginAs(page, accountB, CreatedUserPassword);
    await page.goto('/Course/Index');
    const fullRow = page.locator('tr', { hasText: courseName });
    await expect(fullRow.getByText('Full', { exact: true })).toBeVisible();
    await expect(fullRow.getByRole('button', { name: 'Enroll', exact: true })).toHaveCount(0);

    // Staff path is blocked server-side too: admin cannot enroll student B past the cap.
    await logout(page);
    await login(page);
    await page.goto('/Enrollment/Create');
    await page.locator('#studentId').selectOption({ label: studentBName });
    await page.locator('#courseId').selectOption({ label: courseName });
    const dialogPromise = page.waitForEvent('dialog');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    const dialog = await dialogPromise;
    expect(dialog.message()).toContain('is full');
    await dialog.accept();
    await expect(page).toHaveURL(/\/Enrollment\/Create/);
  });

  test('Teacher dashboard edit preserves the admin-set seat cap', async ({ page }) => {
    const courseName = unique('E2E Cap Course');

    // Teacher creates a course through the dashboard (becomes its owner).
    await loginAs(page, 'teacher@teacher.com', 'Teacher123');
    await page.goto('/Teacher/CreateCourse');
    await page.locator('#Name').fill(courseName);
    await page.locator('#Credits').fill('3');
    await page.getByRole('button', { name: 'Create Course', exact: true }).click();
    await expect(page.locator('#coursesTable')).toContainText(courseName);

    // Admin sets the seat cap on that course.
    await logout(page);
    await login(page);
    await page.goto('/Course/Index');
    const courseId = (await page.locator('tr', { hasText: courseName }).locator('td').first().textContent())!.trim();
    await page.goto(`/Course/Create?id=${courseId}`);
    await page.locator('#max-enrollments').fill('1');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/Course\/Index/);

    // Teacher renames the course via the dashboard. The edit form never posts
    // MaxEnrollments — regression: that used to bind null and wipe the cap.
    await logout(page);
    await loginAs(page, 'teacher@teacher.com', 'Teacher123');
    await page.goto('/Teacher/ManageCourses');
    await page.locator('tr', { hasText: courseName }).getByRole('link', { name: 'Edit', exact: true }).click();
    await page.locator('#Name').fill(`${courseName} II`);
    await page.getByRole('button', { name: 'Update Course', exact: true }).click();
    await expect(page.locator('.alert-success')).toContainText('Course updated successfully.');

    // The admin-set cap survived the teacher edit.
    await logout(page);
    await login(page);
    await page.goto(`/Course/Create?id=${courseId}`);
    await expect(page.locator('#max-enrollments')).toHaveValue('1');
  });
});
