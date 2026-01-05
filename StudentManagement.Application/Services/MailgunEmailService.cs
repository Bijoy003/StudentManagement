using StudentManagement.Application.Interfaces;
using System.Net.Http.Headers;

namespace StudentManagement.Application.Services
{
    public class MailgunEmailService : IMailgunEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _domain;
        private readonly string _fromEmail;

        public MailgunEmailService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _apiKey = config["Mailgun:ApiKey"]!;
            _domain = config["Mailgun:Domain"]!;
            _fromEmail = config["Mailgun:FromEmail"]!;
        }

        public async Task SendMfaCodeHtmlAsync(string toEmail, string mfaCode)
        {
            var subject = "Your verification code";
            var body = $@"
                            <h2>Verification Code</h2>
                            <p>Your MFA code is:</p>
                            <h1>{mfaCode}</h1>
                            <p>This code expires in <strong>5 minutes</strong>.</p>
                            <p>If you did not request this, please ignore this email.</p>";

            await SendEmailAsync(toEmail, subject, body, isHtml: true);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = false)
        {
            return;
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.mailgun.net/v3/{_domain}/messages"
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($"api:{_apiKey}")
                    )
                );

            var content = new Dictionary<string, string>
            {
                { "from", _fromEmail },
                { "to", toEmail },
                { "subject", subject }
            };

            content.Add(isHtml ? "html" : "text", body);

            request.Content = new FormUrlEncodedContent(content);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }
}
