namespace StudentManagement.Domain.Entities
{
    public class EnrollmentArchive
    {
        public int Id { get; set; }
        public int OriginalId { get; set; }
        public int StudentId { get; set; }
        public int CourseId { get; set; }
        public decimal? Grade { get; set; }
        public DateTime ArchivedAt { get; set; }
        public string ArchiveReason { get; set; } = string.Empty;
    }
}