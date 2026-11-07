using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.DTOs;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using System.Text.Json;

namespace StudentManagement.Application.Services
{
    public class CourseService : ICourseService
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IDistributedCache _cache;
        private readonly ILogger<CourseService> _logger;

        public CourseService(ICourseRepository repo, IDistributedCache cache, ILogger<CourseService> logger)
        {
            _courseRepository = repo;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IEnumerable<Course>> GetAllCourses()
        {
            const string cacheKey = "courses_all";
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Cache HIT {CacheKey}", cacheKey);
                return JsonSerializer.Deserialize<List<Course>>(cachedData)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey}", cacheKey);
            var courses = await _courseRepository.GetAllAsyncReadOnly();
            var json = JsonSerializer.Serialize(courses);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
            });
            return courses;
        }

        public async Task<Course?> GetCourseById(int id)
        {
            var cacheKey = $"course_{id}";
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Cache HIT {CacheKey}", cacheKey);
                return JsonSerializer.Deserialize<Course>(cachedData)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey}", cacheKey);
            var course = await _courseRepository.GetByIdAsyncReadOnly(id);
            if (course != null)
            {
                var json = JsonSerializer.Serialize(course);
                await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                });
            }
            return course;
        }

        public async Task SaveCourse(Course course)
        {
            if (course.Id == 0)
            {
                await _courseRepository.AddAsync(course);
            }
            else
            {
                _courseRepository.Update(course);
            }
            await _courseRepository.SaveChangesAsync();
            await InvalidateCacheAsync(course.Id);
        }

        public async Task DeleteCourse(int id)
        {
            await _courseRepository.DeleteAsync(id);
            await _courseRepository.SaveChangesAsync();
            await InvalidateCacheAsync(id);
        }

        private async Task InvalidateCacheAsync(int? courseId = null)
        {
            await RemoveAsync("courses_all");
            await RemoveAsync("course_student_counts");
            if (courseId.HasValue)
            {
                await RemoveAsync($"course_{courseId.Value}");
            }
        }

        private async Task RemoveAsync(string cacheKey)
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogInformation("Cache INVALIDATE {CacheKey}", cacheKey);
        }

        public async Task<List<CourseStudentCountDto>> GetStudentCountPerCourse()
        {
            const string cacheKey = "course_student_counts";
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Cache HIT {CacheKey}", cacheKey);
                return JsonSerializer.Deserialize<List<CourseStudentCountDto>>(cachedData)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey}", cacheKey);
            var counts = await _courseRepository.GetStudentCountPerCourseAsyncReadOnly();
            var json = JsonSerializer.Serialize(counts);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });
            return counts;
        }
    }
}
