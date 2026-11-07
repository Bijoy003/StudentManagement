using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Infrastructure.Data
{
    // Read-only counterpart of ApplicationDbContext. It deliberately has no OnModelCreating override:
    // the schema is owned by ApplicationDbContext, and both contexts must resolve the exact same
    // convention-based model or the two start drifting apart.
    public class ReadOnlyDbContext : DbContext
    {
        public ReadOnlyDbContext(DbContextOptions<ReadOnlyDbContext> options) : base(options)
        {
        }

        public DbSet<Student> Students { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<StudentArchive> StudentsArchive { get; set; }
        public DbSet<EnrollmentArchive> EnrollmentsArchive { get; set; }
    }
}
