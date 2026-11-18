using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Infrastructure.Data
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;

        // Links a Student role account to its own record. SetNull on delete so removing a
        // student does not delete the login.
        public int? StudentId { get; set; }
        public Student? Student { get; set; }
    }

    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Student> Students { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<StudentArchive> StudentsArchive { get; set; }
        public DbSet<EnrollmentArchive> EnrollmentsArchive { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<ChatUsageLog> ChatUsageLogs { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>()
                .HasOne(user => user.Student)
                .WithMany()
                .HasForeignKey(user => user.StudentId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ApplicationUser>()
                .HasIndex(user => user.StudentId)
                .IsUnique();

            // Daily quota lookups always filter by user + UTC day.
            builder.Entity<ChatUsageLog>()
                .HasIndex(log => new { log.UserId, log.UsageDateUtc });

            builder.Entity<ErrorLog>()
                .HasIndex(log => log.TimestampUtc);
        }
    }
}
