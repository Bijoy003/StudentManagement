using System.ComponentModel.DataAnnotations;

namespace StudentMangement.Models
{
    public class MyProfileViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();

        // 2FA
        public bool IsTwoFactorEnabled { get; set; }
    }
}
