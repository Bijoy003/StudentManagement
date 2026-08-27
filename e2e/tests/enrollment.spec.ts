import type { Page } from '@playwright/test';
import { test, expect, login, createStudent, createCourse, unique } from './fixtures';

test.describe('Enrollment', () => {
  test('Index page renders', async ({ page }) => {
    await login(page);
    await page.goto('/Enrollment');
    await expect(page.getByRole('heading', { name: 'Enrollments' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Add Enrollment' })).toBeVisible();
  });

  test('Creates enrollment and sees it in index', async ({ page }) => {
    const studentName = unique('EnrollStudent');
    const studentEmail = `${unique('enrollstu')}@example.com`;
    const courseName = unique('EnrollCourse');

    await login(page);
    await createStudent(page, studentName, studentEmail);
    await createCourse(page, courseName);

    await page.goto('/Enrollment/Create');
    await page.locator('#studentId').selectOption({ label: studentName });
    await page.locator('#courseId').selectOption({ label: courseName });
    await page.locator('#grade').fill('3.5');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page).toHaveURL(/\/Enrollment\/Index/);
    await expect(page.locator('table')).toContainText(studentName, { timeout: 10_000 });
    await expect(page.locator('table')).toContainText(courseName);
    await expect(page.locator('table')).toContainText('3.5');
  });

  test('Students in course filters by course', async ({ page }) => {
    const studentName = unique('InCourse');
    const studentEmail = `${unique('incourse')}@example.com`;
    const courseName = unique('CourseFilter');

    await login(page);
    await createStudent(page, studentName, studentEmail);
    await createCourse(page, courseName);
    await createEnrollment(page, studentName, courseName);

    await page.goto('/Enrollment/StudentsInCourse');
    await page.locator("select[name='courseId']").selectOption({ label: courseName });

    await expect(page.locator('table')).toContainText(studentName, { timeout: 10_000 });
  });

  test('Courses for student filters by student', async ({ page }) => {
    const studentName = unique('ForStudent');
    const studentEmail = `${unique('forstudent')}@example.com`;
    const courseName = unique('StudentCourse');

    await login(page);
    await createStudent(page, studentName, studentEmail);
    await createCourse(page, courseName);
    await createEnrollment(page, studentName, courseName);

    await page.goto('/Enrollment/CoursesForStudent');
    await page.locator("select[name='studentId']").selectOption({ label: studentName });

    await expect(page.locator('table')).toContainText(courseName, { timeout: 10_000 });
  });
});

async function createEnrollment(page: Page, studentName: string, courseName: string): Promise<void> {
  await page.goto('/Enrollment/Create');
  await page.locator('#studentId').selectOption({ label: studentName });
  await page.locator('#courseId').selectOption({ label: courseName });
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/Enrollment\/Index/);
}
