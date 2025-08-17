using System.ComponentModel.DataAnnotations;

namespace StudentMangement.Models
{
    public class Student
    {
        [Key]
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public DateTime? DateOfEnroll { get; set; }
    }
}
