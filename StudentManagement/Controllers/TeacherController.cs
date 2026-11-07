using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Web.Controllers;

[Authorize(Roles = "Teacher")]
public class TeacherController : Controller
{
    private readonly ITeacherService _teacherService;
    private readonly ILogger<TeacherController> _logger;

    public TeacherController(
        ITeacherService teacherService,
        ILogger<TeacherController> logger)
    {
        _teacherService = teacherService;
        _logger = logger;
    }

    // Teacher identity is the signed-in username, consistent with how Course.TeacherId is stamped.
    private string TeacherUserName => User.Identity!.Name!;

    public async Task<IActionResult> Dashboard()
    {
        var userId = TeacherUserName;
        var courses = await _teacherService.GetTeacherCoursesAsync(userId);

        ViewBag.CourseCount = courses.Count;
        ViewBag.TotalStudents = courses.Sum(c => c.Enrollments?.Count ?? 0);
        ViewBag.Courses = courses;

        return View();
    }

    public async Task<IActionResult> ManageCourses()
    {
        var userId = TeacherUserName;
        var courses = await _teacherService.GetTeacherCoursesAsync(userId);
        return View(courses);
    }

    [HttpGet]
    public IActionResult CreateCourse()
    {
        var userId = TeacherUserName;
        return View(new Course { TeacherId = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCourse(Course course)
    {
        if (!ModelState.IsValid)
        {
            return View(course);
        }

        var userId = TeacherUserName;
        course.TeacherId = userId;

        try
        {
            var saved = await _teacherService.SaveCourseAsync(course, userId);
            if (!saved)
            {
                TempData["Error"] = "The course could not be created.";
                return RedirectToAction("ManageCourses");
            }

            TempData["Success"] = "Course created successfully.";
            return RedirectToAction("ManageCourses");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating course: {CourseName}", course.Name);
            TempData["Error"] = "An error occurred while creating the course.";
            return View(course);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        var userId = TeacherUserName;
        var course = await _teacherService.GetCourseByIdAsync(id);
        if (course == null || course.TeacherId != userId)
            return Forbid();

        return View(course);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourse(Course course)
    {
        if (!ModelState.IsValid)
        {
            return View(course);
        }

        var userId = TeacherUserName;
        course.TeacherId = userId;

        try
        {
            var saved = await _teacherService.SaveCourseAsync(course, userId);
            if (!saved)
            {
                TempData["Error"] = "You can only edit courses that belong to you.";
                return RedirectToAction("ManageCourses");
            }

            TempData["Success"] = "Course updated successfully.";
            return RedirectToAction("ManageCourses");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating course: {CourseName}", course.Name);
            TempData["Error"] = "An error occurred while updating the course.";
            return View(course);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        try
        {
            var userId = TeacherUserName;
            var deleted = await _teacherService.DeleteCourseAsync(id, userId);
            if (!deleted)
            {
                return Json(new { success = false, message = "Course not found or you do not own it." });
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting course: {Id}", id);
            return Json(new { success = false, message = "An error occurred while deleting the course." });
        }
    }

    public async Task<IActionResult> ManageGrades(int? courseId)
    {
        var userId = TeacherUserName;
        var courses = await _teacherService.GetTeacherCoursesAsync(userId);
        ViewBag.Courses = courses;

        List<Enrollment> enrollments = new();
        if (courseId.HasValue)
        {
            var course = await _teacherService.GetCourseByIdAsync(courseId.Value);
            if (course == null || course.TeacherId != userId)
                return Forbid();

            enrollments = await _teacherService.GetEnrollmentsByCourseAsync(courseId.Value);
            ViewBag.SelectedCourseId = courseId;
        }

        ViewBag.Enrollments = enrollments;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGrade(int enrollmentId, [Range(0, 100)] decimal? grade, int? courseId = null)
    {
        // Non-numeric input fails to bind and lands here as a ModelState error instead of
        // silently arriving as null (which would clear the grade with a success flash).
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrEmpty(m));

            TempData["Error"] = firstError ?? "The grade must be a number between 0 and 100.";
            return RedirectToAction("ManageGrades", new { courseId });
        }

        try
        {
            var userId = TeacherUserName;
            var updated = await _teacherService.UpdateGradeAsync(enrollmentId, grade, userId);
            if (!updated)
            {
                TempData["Error"] = "You can only update grades for your own courses.";
                return RedirectToAction("ManageGrades", new { courseId });
            }

            TempData["Success"] = "Grade updated successfully.";
            return RedirectToAction("ManageGrades", new { courseId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating grade for enrollment: {EnrollmentId}", enrollmentId);
            TempData["Error"] = "An error occurred while updating the grade.";
            return RedirectToAction("ManageGrades", new { courseId });
        }
    }

    public async Task<IActionResult> CourseStudents(int courseId)
    {
        var userId = TeacherUserName;
        var course = await _teacherService.GetCourseByIdAsync(courseId);
        if (course == null || course.TeacherId != userId)
            return Forbid();

        var enrollments = await _teacherService.GetEnrollmentsByCourseAsync(courseId);
        ViewBag.Course = course;
        ViewBag.Enrollments = enrollments;
        // The view renders from ViewBag only (no @model), so no model instance is passed.
        return View();
    }
}
