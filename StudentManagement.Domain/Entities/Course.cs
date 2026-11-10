using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace StudentManagement.Domain.Entities
{
    public class Course
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
        public string Name { get; set; } = string.Empty;

        public int Credits { get; set; }

        public GradeMode GradeMode { get; set; } = GradeMode.Letter;

        public string? TeacherId { get; set; }

        // Reverse navigation: excluded from System.Text.Json so serializing an Enrollment
        // (which keeps Student/Course for the cached JSON) cannot walk back into a cycle.
        [JsonIgnore]
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}
