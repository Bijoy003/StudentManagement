using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using System.Text.Json;

namespace StudentManagement.Application.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

        private const string AllEnrollmentsCacheKey = "enrollments_all";
        private const string CourseStudentCountsCacheKey = "course_student_counts";

        private readonly IEnrollmentRepository _enrollmentRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache _cache;
        private readonly ILogger<EnrollmentService> _logger;

        public EnrollmentService(
            IEnrollmentRepository repo,
            IMemoryCache memoryCache,
            IDistributedCache distributedCache,
            ILogger<EnrollmentService> logger)
        {
            _enrollmentRepository = repo;
            _memoryCache = memoryCache;
            _cache = distributedCache;
            _logger = logger;
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollments()
        {
            if (_memoryCache.TryGetValue(AllEnrollmentsCacheKey, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", AllEnrollmentsCacheKey);
                return JsonSerializer.Deserialize<List<Enrollment>>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", AllEnrollmentsCacheKey);
            var enrollments = await _enrollmentRepository.GetAllAsyncReadOnly();
            _memoryCache.Set(AllEnrollmentsCacheKey, JsonSerializer.Serialize(enrollments), CacheDuration);
            return enrollments;
        }

        public async Task<Enrollment?> GetEnrollmentById(int id)
        {
            var cacheKey = $"enrollment_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", cacheKey);
                return JsonSerializer.Deserialize<Enrollment>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", cacheKey);
            var enrollment = await _enrollmentRepository.GetByIdAsyncReadOnly(id);
            if (enrollment != null)
            {
                _memoryCache.Set(cacheKey, JsonSerializer.Serialize(enrollment), CacheDuration);
            }
            return enrollment;
        }

        public async Task SaveEnrollment(Enrollment enrollment)
        {
            if (enrollment.Id == 0)
            {
                await _enrollmentRepository.AddAsync(enrollment);
            }
            else
            {
                _enrollmentRepository.Update(enrollment);
            }
            await _enrollmentRepository.SaveChangesAsync();
            await InvalidateCacheAsync(enrollment.Id, enrollment.CourseId, enrollment.StudentId);
        }

        public async Task DeleteEnrollment(int id)
        {
            var enrollment = await _enrollmentRepository.GetByIdAsync(id);

            await _enrollmentRepository.DeleteAsync(id);
            await _enrollmentRepository.SaveChangesAsync();

            if (enrollment == null)
            {
                RemoveFromMemoryCache(AllEnrollmentsCacheKey);
                RemoveFromMemoryCache($"enrollment_{id}");
                await RemoveAsync(CourseStudentCountsCacheKey);
                return;
            }

            await InvalidateCacheAsync(id, enrollment.CourseId, enrollment.StudentId);
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollmentsByCourse(int courseId)
        {
            var cacheKey = $"enrollment_course_{courseId}";
            if (_memoryCache.TryGetValue(cacheKey, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", cacheKey);
                return JsonSerializer.Deserialize<List<Enrollment>>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", cacheKey);
            var enrollments = await _enrollmentRepository.GetByCourseIdAsyncReadOnly(courseId);
            _memoryCache.Set(cacheKey, JsonSerializer.Serialize(enrollments), CacheDuration);
            return enrollments;
        }

        public async Task<List<Student>> GetStudentsInCourse(int courseId)
        {
            var cacheKey = $"enrollment_students_course_{courseId}";
            if (_memoryCache.TryGetValue(cacheKey, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", cacheKey);
                return JsonSerializer.Deserialize<List<Student>>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", cacheKey);
            var students = await _enrollmentRepository.GetStudentsByCourseAsyncReadOnly(courseId);
            _memoryCache.Set(cacheKey, JsonSerializer.Serialize(students), CacheDuration);
            return students;
        }

        public async Task<List<Course>> GetCoursesForStudent(int studentId)
        {
            var cacheKey = $"enrollment_courses_student_{studentId}";
            if (_memoryCache.TryGetValue(cacheKey, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", cacheKey);
                return JsonSerializer.Deserialize<List<Course>>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", cacheKey);
            var courses = await _enrollmentRepository.GetCoursesByStudentAsyncReadOnly(studentId);
            _memoryCache.Set(cacheKey, JsonSerializer.Serialize(courses), CacheDuration);
            return courses;
        }

        private async Task InvalidateCacheAsync(int id, int? courseId = null, int? studentId = null)
        {
            RemoveFromMemoryCache(AllEnrollmentsCacheKey);
            RemoveFromMemoryCache($"enrollment_{id}");
            if (courseId.HasValue)
            {
                RemoveFromMemoryCache($"enrollment_course_{courseId.Value}");
                RemoveFromMemoryCache($"enrollment_students_course_{courseId.Value}");
            }
            if (studentId.HasValue)
            {
                RemoveFromMemoryCache($"enrollment_courses_student_{studentId.Value}");
            }
            await RemoveAsync(CourseStudentCountsCacheKey);
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
}
