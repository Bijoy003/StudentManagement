using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Validation;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace StudentManagement.Application.Services
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _studentRepository;
        private readonly IEnrollmentRepository _enrollmentRepository;
        private readonly IDistributedCache _cache;
        private readonly ILogger<StudentService> _logger;

        // Head of the Chain of Responsibility validation pipeline
        // (RequiredFieldsRule -> EmailFormatRule -> DuplicateEmailRule). Optional so the
        // legacy 4-arg construction (existing tests + DI fallback) keeps saving unvalidated.
        private readonly IValidationHandler? _validationChain;

        public StudentService(
            IStudentRepository studentRepository,
            IEnrollmentRepository enrollmentRepository,
            IDistributedCache cache,
            ILogger<StudentService> logger,
            IValidationHandler? validationChain = null)
        {
            _studentRepository = studentRepository;
            _enrollmentRepository = enrollmentRepository;
            _cache = cache;
            _logger = logger;
            _validationChain = validationChain;
        }

        public async Task<IEnumerable<Student>> GetStudentsAsync()
        {
            const string cacheKey = CacheKeys.AllStudents;
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Cache HIT {CacheKey}", cacheKey);
                return JsonSerializer.Deserialize<List<Student>>(cachedData)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey}", cacheKey);
            var students = await _studentRepository.GetAllAsyncReadOnly();
            var json = JsonSerializer.Serialize(students);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
            });
            return students;
        }

        public async Task<Student?> GetStudentByIdAsync(int id)
        {
            var cacheKey = CacheKeys.Student(id);
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Cache HIT {CacheKey}", cacheKey);
                return JsonSerializer.Deserialize<Student>(cachedData)!;
            }

            _logger.LogInformation("Cache MISS {CacheKey}", cacheKey);
            var student = await _studentRepository.GetByIdAsyncReadOnly(id);
            if (student != null)
            {
                var json = JsonSerializer.Serialize(student);
                await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                });
            }
            return student;
        }

        public async Task SaveStudentAsync(Student student)
        {
            if (_validationChain is not null)
            {
                var error = await _validationChain.ValidateAsync(student);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new ValidationException(error);
                }
            }

            if (student.Id == 0)
            {
                await _studentRepository.AddAsync(student);
            }
            else
            {
                _studentRepository.Update(student);
            }
            await _studentRepository.SaveAsync();
            await InvalidateCacheAsync(student.Id);
        }

        public async Task DeleteStudentAsync(int id)
        {
            await _studentRepository.DeleteAsync(id);
            await _studentRepository.SaveAsync();
            await InvalidateCacheAsync(id);
        }

        private async Task InvalidateCacheAsync(int? studentId = null)
        {
            await RemoveAsync(CacheKeys.AllStudents);
            if (studentId.HasValue)
            {
                await RemoveAsync(CacheKeys.Student(studentId.Value));
            }
        }

        private async Task RemoveAsync(string cacheKey)
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogInformation("Cache INVALIDATE {CacheKey}", cacheKey);
        }

        public async Task<List<Student>> GetStudentsEnrolledInMoreThan(int courseCount)
        {
            return await _enrollmentRepository.GetStudentsEnrolledInMoreThanAsyncReadOnly(courseCount);
        }
    }
}
