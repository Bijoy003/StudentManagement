using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Events;
using StudentManagement.Domain.Interfaces;
using System.Text.Json;

namespace StudentManagement.Application.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

        // Shared fallback so GetGradingStrategy never returns null/throws: unknown modes
        // and a construction without injected strategies both degrade to Letter grading.
        private static readonly LetterGradeStrategy LetterFallback = new();

        private readonly IEnrollmentRepository _enrollmentRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache _cache;
        private readonly ILogger<EnrollmentService> _logger;

        // Optional so the legacy 4-arg construction (existing tests) stays source-compatible.
        private readonly IDomainEventDispatcher? _domainEventDispatcher;
        private readonly IEnumerable<IGradingStrategy>? _gradingStrategies;

        public EnrollmentService(
            IEnrollmentRepository repo,
            IMemoryCache memoryCache,
            IDistributedCache distributedCache,
            ILogger<EnrollmentService> logger,
            IDomainEventDispatcher? domainEventDispatcher = null,
            IEnumerable<IGradingStrategy>? gradingStrategies = null)
        {
            _enrollmentRepository = repo;
            _memoryCache = memoryCache;
            _cache = distributedCache;
            _logger = logger;
            _domainEventDispatcher = domainEventDispatcher;
            _gradingStrategies = gradingStrategies;
        }

        public IGradingStrategy GetGradingStrategy(GradeMode gradeMode) =>
            _gradingStrategies?.FirstOrDefault(strategy => strategy.Mode == gradeMode) ?? LetterFallback;

        public string ComputeGradeDisplay(decimal? score, GradeMode gradeMode) =>
            GetGradingStrategy(gradeMode).ComputeGrade(score);

        public async Task<IEnumerable<Enrollment>> GetEnrollments()
        {
            if (_memoryCache.TryGetValue(CacheKeys.AllEnrollments, out var cached)
                && cached is string cachedJson)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", CacheKeys.AllEnrollments);
                return JsonSerializer.Deserialize<List<Enrollment>>(cachedJson)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", CacheKeys.AllEnrollments);
            var enrollments = await _enrollmentRepository.GetAllAsyncReadOnly();
            _memoryCache.Set(CacheKeys.AllEnrollments, JsonSerializer.Serialize(enrollments), CacheDuration);
            return enrollments;
        }

        public async Task<Enrollment?> GetEnrollmentById(int id)
        {
            var cacheKey = CacheKeys.Enrollment(id);
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
            var isNew = enrollment.Id == 0;
            if (isNew)
            {
                await _enrollmentRepository.AddAsync(enrollment);
            }
            else
            {
                _enrollmentRepository.Update(enrollment);
            }
            await _enrollmentRepository.SaveChangesAsync();
            await InvalidateCacheAsync(enrollment.Id, enrollment.CourseId, enrollment.StudentId);

            if (isNew && _domainEventDispatcher is not null)
            {
                await _domainEventDispatcher.DispatchAsync(
                    new EnrollmentCreatedEvent(enrollment.Id, enrollment.CourseId, enrollment.StudentId));
            }
        }

        public async Task DeleteEnrollment(int id)
        {
            var enrollment = await _enrollmentRepository.GetByIdAsync(id);

            await _enrollmentRepository.DeleteAsync(id);
            await _enrollmentRepository.SaveChangesAsync();

            if (enrollment == null)
            {
                RemoveFromMemoryCache(CacheKeys.AllEnrollments);
                RemoveFromMemoryCache(CacheKeys.Enrollment(id));
                await RemoveAsync(CacheKeys.CourseStudentCounts);
                return;
            }

            await InvalidateCacheAsync(id, enrollment.CourseId, enrollment.StudentId);

            if (_domainEventDispatcher is not null)
            {
                await _domainEventDispatcher.DispatchAsync(
                    new EnrollmentDeletedEvent(id, enrollment.CourseId, enrollment.StudentId));
            }
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollmentsByCourse(int courseId)
        {
            var cacheKey = CacheKeys.EnrollmentsByCourse(courseId);
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
            var cacheKey = CacheKeys.StudentsInCourse(courseId);
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
            var cacheKey = CacheKeys.CoursesForStudent(studentId);
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
            RemoveFromMemoryCache(CacheKeys.AllEnrollments);
            RemoveFromMemoryCache(CacheKeys.Enrollment(id));
            if (courseId.HasValue)
            {
                RemoveFromMemoryCache(CacheKeys.EnrollmentsByCourse(courseId.Value));
                RemoveFromMemoryCache(CacheKeys.StudentsInCourse(courseId.Value));
            }
            if (studentId.HasValue)
            {
                RemoveFromMemoryCache(CacheKeys.CoursesForStudent(studentId.Value));
            }
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
}
