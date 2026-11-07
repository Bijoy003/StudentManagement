using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Services
{
    public class ArchiveService : IArchiveService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<ArchiveService> _logger;

        public ArchiveService(ApplicationDbContext dbContext, ILogger<ArchiveService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<int> ArchiveOldEnrollmentsAsync(int yearsOld = 1)
        {
            var cutoffDate = DateTime.UtcNow.AddYears(-yearsOld);

            var oldEnrollments = await _dbContext.Enrollments
                .Where(e => e.Student.DateOfEnroll.HasValue && e.Student.DateOfEnroll.Value < cutoffDate)
                .ToListAsync();

            if (oldEnrollments.Count == 0)
            {
                _logger.LogInformation("No enrollments older than {Years} years to archive", yearsOld);
                return 0;
            }

            var archives = oldEnrollments.Select(e => new EnrollmentArchive
            {
                OriginalId = e.Id,
                StudentId = e.StudentId,
                CourseId = e.CourseId,
                Grade = e.Grade,
                ArchivedAt = DateTime.UtcNow,
                ArchiveReason = $"Auto-archived: enrollment older than {yearsOld} year(s)"
            }).ToList();

            await _dbContext.EnrollmentsArchive.AddRangeAsync(archives);
            _dbContext.Enrollments.RemoveRange(oldEnrollments);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Archived {Count} enrollments older than {Years} years", archives.Count, yearsOld);
            return archives.Count;
        }

        public async Task<int> ArchiveOldStudentsAsync(int yearsOld = 1)
        {
            var cutoffDate = DateTime.UtcNow.AddYears(-yearsOld);

            var oldStudents = await _dbContext.Students
                .Where(s => s.DateOfEnroll.HasValue && s.DateOfEnroll.Value < cutoffDate
                         && !s.Enrollments.Any())
                .ToListAsync();

            if (oldStudents.Count == 0)
            {
                _logger.LogInformation("No students older than {Years} years without enrollments to archive", yearsOld);
                return 0;
            }

            var archives = oldStudents.Select(s => new StudentArchive
            {
                OriginalId = s.Id,
                Name = s.Name ?? string.Empty,
                Email = s.Email ?? string.Empty,
                Phone = s.Phone,
                Address = s.Address,
                DateOfEnroll = s.DateOfEnroll,
                ArchivedAt = DateTime.UtcNow,
                ArchiveReason = $"Auto-archived: student enrolled more than {yearsOld} year(s) ago with no enrollments"
            }).ToList();

            await _dbContext.StudentsArchive.AddRangeAsync(archives);
            _dbContext.Students.RemoveRange(oldStudents);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Archived {Count} students older than {Years} years with no enrollments", archives.Count, yearsOld);
            return archives.Count;
        }

        public async Task<ArchiveStats> GetArchiveStatsAsync()
        {
            var totalStudents = await _dbContext.Students.CountAsync();
            var totalEnrollments = await _dbContext.Enrollments.CountAsync();
            var archivedStudents = await _dbContext.StudentsArchive.CountAsync();
            var archivedEnrollments = await _dbContext.EnrollmentsArchive.CountAsync();

            var cutoffDate = DateTime.UtcNow.AddYears(-1);
            var studentsToArchive = await _dbContext.Students
                .Where(s => s.DateOfEnroll.HasValue && s.DateOfEnroll.Value < cutoffDate
                         && !s.Enrollments.Any())
                .CountAsync();
            var enrollmentsToArchive = await _dbContext.Enrollments
                .Where(e => e.Student.DateOfEnroll.HasValue && e.Student.DateOfEnroll.Value < cutoffDate)
                .CountAsync();

            return new ArchiveStats
            {
                TotalStudents = totalStudents,
                TotalEnrollments = totalEnrollments,
                ArchivedStudents = archivedStudents,
                ArchivedEnrollments = archivedEnrollments,
                StudentsToArchive = studentsToArchive,
                EnrollmentsToArchive = enrollmentsToArchive
            };
        }
    }
}