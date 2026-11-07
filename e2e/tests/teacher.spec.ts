import { test, expect, login, loginAs, logout, createStudent, unique } from './fixtures';

const TeacherEmail = 'teacher@teacher.com';
const TeacherPassword = 'Teacher123';

test.describe('Teacher Dashboard', () => {
  test('Teacher creates a course, grades the roster, and deletes it', async ({ page }) => {
    const courseName = unique('TeacherCourse');
    const studentName = unique('TeacherStudent');
    const studentEmail = `${unique('teacherstu')}@example.com`;

    // 1. Teacher logs in: the role-gated navbar link appears and the dashboard renders.
    await loginAs(page, TeacherEmail, TeacherPassword);
    const navLink = page.locator('.navbar').getByRole('link', { name: 'Teacher Dashboard' });
    await expect(navLink).toBeVisible();
    await navLink.click();
    await expect(page).toHaveURL(/\/Teacher\/Dashboard/);
    await expect(page.getByRole('heading', { name: 'Teacher Dashboard' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'My Courses' }).first()).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Total Students' })).toBeVisible();

    // 2. Create a course through the Teacher UI; it shows up in Manage Courses.
    await page.goto('/Teacher/CreateCourse');
    await page.locator('#Name').fill(courseName);
    await page.locator('#Credits').fill('4');
    await page.getByRole('button', { name: 'Create Course', exact: true }).click();
    await expect(page).toHaveURL(/\/Teacher\/ManageCourses/);
    await expect(page.locator('#coursesTable')).toContainText(courseName);

    // 3. Admin creates a student and enrolls them in the teacher's course.
    await logout(page);
    await login(page);
    await createStudent(page, studentName, studentEmail);
    await page.goto('/Enrollment/Create');
    await page.locator('#studentId').selectOption({ label: studentName });
    await page.locator('#courseId').selectOption({ label: courseName });
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/Enrollment\/Index/);
    await expect(page.locator('table')).toContainText(courseName, { timeout: 10_000 });

    // 4. Teacher grades the student. Regression: one form used to wrap every row (so Save
    //    posted all enrollmentIds and updated the wrong row) and the antiforgery token was
    //    stringified into its own value (400). Each row now has its own valid form.
    await logout(page);
    await loginAs(page, TeacherEmail, TeacherPassword);
    await page.goto('/Teacher/ManageGrades');
    await page.locator("select[name='courseId']").selectOption({ label: courseName });

    const rosterRow = page.locator('tr', { hasText: studentName });
    await expect(rosterRow).toBeVisible({ timeout: 10_000 });
    await expect(rosterRow).toContainText(studentEmail);

    await rosterRow.locator("input[name='grade']").fill('87.5');
    await rosterRow.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page).toHaveURL(/\/Teacher\/ManageGrades/);
    await expect(page.locator('.alert-success')).toContainText('Grade updated successfully.');

    // The grade persists: re-selecting the course shows 87.50 and refills the input.
    await page.locator("select[name='courseId']").selectOption({ label: courseName });
    await expect(rosterRow).toContainText('87.50', { timeout: 10_000 });
    await expect(rosterRow.locator("input[name='grade']")).toHaveValue('87.5');

    // 5. Delete the course from Manage Courses. Regression: deleteCourse() used to
    //    null-ref on a token input that was never rendered; the token now lives in
    //    #deleteForm and is sent via the RequestVerificationToken header.
    await page.goto('/Teacher/ManageCourses');
    await expect(page.locator('#coursesTable')).toContainText(courseName);
    page.once('dialog', dialog => dialog.accept());
    await page
      .locator('#coursesTable tr', { hasText: courseName })
      .getByRole('button', { name: 'Delete' })
      .click();
    await expect(page.locator('#coursesTable tr', { hasText: courseName })).toHaveCount(0, { timeout: 10_000 });
  });
});
