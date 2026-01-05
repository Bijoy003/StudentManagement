using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Web.Models
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

        // Authenticator setup
        public bool ShowAuthenticatorSetup { get; set; }    // controls QR visibility
        public string? SharedKey { get; set; }              // manual entry key
        public string? QrCodeUri { get; set; }              // otpauth:// URI
        public string? MfaCode { get; set; }                // user-entered 6-digit code
    }
}
