namespace StudentManagement.Domain.Interfaces
{
    public interface IArchiveService
    {
        Task<int> ArchiveOldEnrollmentsAsync(int yearsOld = 1);
        Task<int> ArchiveOldStudentsAsync(int yearsOld = 1);
        Task<ArchiveStats> GetArchiveStatsAsync();
    }

    public class ArchiveStats
    {
        public int TotalStudents { get; set; }
        public int TotalEnrollments { get; set; }
        public int ArchivedStudents { get; set; }
        public int ArchivedEnrollments { get; set; }
        public int StudentsToArchive { get; set; }
        public int EnrollmentsToArchive { get; set; }
    }
}