namespace StudentManagement.Web.Models
{
    public class LoginViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }

        // MFA
        public bool IsMfaRequired { get; set; } = false;
        public string? MfaCode { get; set; }
    }

}
