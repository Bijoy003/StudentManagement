using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace StudentManagement.Domain.Entities
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(50, ErrorMessage = "Name cannot be longer than 50 characters")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
        public string? Address { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateOfEnroll { get; set; }

        // Reverse navigation: excluded from System.Text.Json so serializing an Enrollment
        // (which keeps Student/Course for the cached JSON) cannot walk back into a cycle.
        [JsonIgnore]
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}
