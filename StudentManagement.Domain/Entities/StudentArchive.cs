namespace StudentManagement.Domain.Entities
{
    public class StudentArchive
    {
        public int Id { get; set; }
        public int OriginalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public DateTime? DateOfEnroll { get; set; }
        public DateTime ArchivedAt { get; set; }
        public string ArchiveReason { get; set; } = string.Empty;
    }
}