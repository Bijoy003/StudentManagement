namespace StudentManagement.Application.Interfaces
{
    public interface IMailgunEmailService
    {
        Task SendMfaCodeHtmlAsync(string toEmail, string mfaCode);
        Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = false);
    }
}
