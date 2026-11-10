using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services;

public class TeacherService : ITeacherService
{
    // Cache keys come from CacheKeys (single source of truth): CourseService/EnrollmentService
    // own the entries, and TeacherService writes must evict the exact same keys or the other
    // services keep serving stale data (e.g. a teacher-created course missing from the
    // /Enrollment/Create dropdown for up to the 60 s TTL).
    private readonly ICourseRepository _courseRepository;
    private readonly IEnrollmentRepository _enrollmentRepository;
    private readonly IDistributedCache _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(
        ICourseRepository courseRepository,
        IEnrollmentRepository enrollmentRepository,
        IDistributedCache cache,
        IMemoryCache memoryCache,
        ILogger<TeacherService> logger)
    {
        _courseRepository = courseRepository;
        _enrollmentRepository = enrollmentRepository;
        _cache = cache;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<List<Course>> GetTeacherCoursesAsync(string teacherId)
    {
        return await _courseRepository.GetByTeacherIdAsyncReadOnly(teacherId);
    }

    public async Task<Course?> GetCourseByIdAsync(int courseId)
    {
        return await _courseRepository.GetByIdAsync(courseId);
    }

    public async Task<bool> SaveCourseAsync(Course course, string teacherId)
    {
        // Defensive: never attach a caller-supplied enrollment graph to the write context.
        course.Enrollments = new List<Enrollment>();

        if (course.Id == 0)
        {
            await _courseRepository.AddAsync(course);
            await _courseRepository.SaveChangesAsync();
            await InvalidateCourseCacheAsync(course.Id);
            return true;
        }

        var persisted = await _courseRepository.GetByIdAsyncReadOnly(course.Id);
        if (persisted == null || persisted.TeacherId != teacherId)
        {
            return false;
        }

        _courseRepository.Update(course);
        await _courseRepository.SaveChangesAsync();
        await InvalidateCourseCacheAsync(course.Id);
        return true;
    }

    public async Task<bool> DeleteCourseAsync(int courseId, string teacherId)
    {
        var persisted = await _courseRepository.GetByIdAsyncReadOnly(courseId);
        if (persisted == null || persisted.TeacherId != teacherId)
        {
            return false;
        }

        await _courseRepository.DeleteAsync(courseId);
        await _courseRepository.SaveChangesAsync();
        await InvalidateCourseCacheAsync(courseId);
        // Deleting a course cascades its enrollment rows in the database, so the
        // enrollment caches keyed on this course (and the full list) are stale too.
        // Individual "enrollment_{id}" entries are not enumerable here and expire
        // within their 60 s TTL; the same rationale covers the per-student
        // "enrollment_courses_student_{studentId}" entries of the enrolled students,
        // which are likewise not enumerable from a course id.
        RemoveFromMemoryCache(CacheKeys.AllEnrollments);
        RemoveFromMemoryCache(CacheKeys.EnrollmentsByCourse(courseId));
        RemoveFromMemoryCache(CacheKeys.StudentsInCourse(courseId));
        return true;
    }

    public async Task<List<Enrollment>> GetEnrollmentsByCourseAsync(int courseId)
    {
        return await _enrollmentRepository.GetByCourseIdWithStudentAsyncReadOnly(courseId);
    }

    public async Task<bool> UpdateGradeAsync(int enrollmentId, decimal? grade, string teacherId)
    {
        // Server-side range guard: grades live on a 0-100 scale (null clears the grade).
        if (grade.HasValue && (grade.Value < 0m || grade.Value > 100m))
        {
            return false;
        }

        var enrollment = await _enrollmentRepository.GetByIdAsync(enrollmentId);
        if (enrollment == null)
        {
            return false;
        }

        var course = await _courseRepository.GetByIdAsyncReadOnly(enrollment.CourseId);
        if (course == null || course.TeacherId != teacherId)
        {
            return false;
        }

        enrollment.Grade = grade;
        _enrollmentRepository.Update(enrollment);
        await _enrollmentRepository.SaveChangesAsync();
        await InvalidateEnrollmentCacheAsync(enrollment.Id, enrollment.CourseId, enrollment.StudentId);
        return true;
    }

    // Mirrors CourseService.InvalidateCacheAsync for a single course write.
    private async Task InvalidateCourseCacheAsync(int courseId)
    {
        await RemoveAsync(CacheKeys.AllCourses);
        await RemoveAsync(CacheKeys.CourseStudentCounts);
        await RemoveAsync(CacheKeys.Course(courseId));
    }

    // Mirrors EnrollmentService.InvalidateCacheAsync for a single enrollment write.
    private async Task InvalidateEnrollmentCacheAsync(int enrollmentId, int courseId, int studentId)
    {
        RemoveFromMemoryCache(CacheKeys.AllEnrollments);
        RemoveFromMemoryCache(CacheKeys.Enrollment(enrollmentId));
        RemoveFromMemoryCache(CacheKeys.EnrollmentsByCourse(courseId));
        RemoveFromMemoryCache(CacheKeys.StudentsInCourse(courseId));
        RemoveFromMemoryCache(CacheKeys.CoursesForStudent(studentId));
        await RemoveAsync(CacheKeys.CourseStudentCounts);
    }

    private void RemoveFromMemoryCache(string cacheKey)
    {
        _memoryCache.Remove(cacheKey);
        _logger.LogInformation("Cache INVALIDATE {CacheKey} (in-process)", cacheKey);
    }

    private async Task RemoveAsync(string cacheKey)
    {
        await _cache.RemoveAsync(cacheKey);
        _logger.LogInformation("Cache INVALIDATE {CacheKey}", cacheKey);
    }
}
