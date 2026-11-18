using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Web.Models
{
    // Admin/Users page: every Identity account with its roles and student link.
    public class UserManagementViewModel
    {
        public List<string> Roles { get; set; } = [];
        public List<StudentOptionViewModel> Students { get; set; } = [];
        public List<UserRowViewModel> Users { get; set; } = [];
    }

    public class UserRowViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public int? StudentId { get; set; }
        public string? StudentName { get; set; }
    }

    public class StudentOptionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SetUserRoleViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;
    }

    public class ResetUserPasswordViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class LinkUserStudentViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        // Null unlinks the account from its student record.
        [Display(Name = "Student record")]
        public int? StudentId { get; set; }
    }
}
