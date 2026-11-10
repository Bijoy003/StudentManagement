using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

// WP8 AC9 — MailgunEmailService send path gated by Mailgun:Enabled.
// Disabled -> log and return without any HTTP attempt (replacing the unreachable-code
// `return;` that trips CS0162). Enabled -> actually build and send the Mailgun request.
public class MailgunEmailServiceTests
{
    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = new();
        public List<string> RequestMethods { get; } = new();
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            RequestMethods.Add(request.Method.Method);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static IConfiguration CreateConfig(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mailgun:ApiKey"] = "stub-api-key",
                ["Mailgun:Domain"] = "mail.example.test",
                ["Mailgun:FromEmail"] = "noreply@example.test",
                ["Mailgun:Enabled"] = enabled ? "true" : "false"
            })
            .Build();

    private static readonly MockBehavior LoggerBehavior = MockBehavior.Loose;

    private static (MailgunEmailService Service, RecordingHttpMessageHandler Handler, Mock<ILogger<MailgunEmailService>> Logger)
        CreateService(bool enabled)
    {
        var handler = new RecordingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<MailgunEmailService>>(LoggerBehavior);
        var service = new MailgunEmailService(httpClient, CreateConfig(enabled), logger.Object);
        return (service, handler, logger);
    }

    private static void VerifyAnyLog(Mock<ILogger<MailgunEmailService>> logger)
    {
        logger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task SendEmailAsync_MailgunDisabled_ReturnsNoHttpRequestAttemptedAndLogs()
    {
        // Arrange
        var (service, handler, logger) = CreateService(enabled: false);

        // Act
        await service.SendEmailAsync("user@example.test", "Subject", "Body", isHtml: false);

        // Assert — RED today: nothing is logged (silent `return;`)
        Assert.Empty(handler.RequestUris);
        VerifyAnyLog(logger);
    }

    [Fact]
    public async Task SendEmailAsync_MailgunEnabled_ReturnsPostSentToMailgunMessagesEndpoint()
    {
        // Arrange
        var (service, handler, _) = CreateService(enabled: true);

        // Act
        await service.SendEmailAsync("user@example.test", "Hello", "Plain body", isHtml: false);

        // Assert — RED today: unreachable code after the stubbed early return
        var uri = Assert.Single(handler.RequestUris);
        Assert.Equal("POST", Assert.Single(handler.RequestMethods));
        Assert.EndsWith("/v3/mail.example.test/messages", uri);
        var body = Assert.Single(handler.RequestBodies);
        // FormUrlEncodedContent escapes '@' as %40 and spaces as '+'
        Assert.Contains("to=user%40example.test", body);
        Assert.Contains("text=Plain+body", body);
    }

    [Fact]
    public async Task SendEmailAsync_MailgunEnabledHtmlBody_ReturnsFormWithHtmlFieldAndFromAddress()
    {
        // Arrange
        var (service, handler, _) = CreateService(enabled: true);

        // Act
        await service.SendEmailAsync("user@example.test", "Subject line", "<b>Hi</b>", isHtml: true);

        // Assert
        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("from=noreply", body);
        Assert.Contains("Subject+line", body);
        Assert.Contains("html=", body);
        Assert.DoesNotContain("text=", body); // isHtml routes the body to the html field
    }

    [Fact]
    public async Task SendMfaCodeHtmlAsync_MailgunDisabled_ReturnsNoHttpRequestAttemptedAndLogs()
    {
        // Arrange
        var (service, handler, logger) = CreateService(enabled: false);

        // Act
        await service.SendMfaCodeHtmlAsync("user@example.test", "123456");

        // Assert — the gate must cover the MFA convenience wrapper too
        Assert.Empty(handler.RequestUris);
        VerifyAnyLog(logger);
    }

    [Fact]
    public async Task SendMfaCodeHtmlAsync_MailgunEnabled_ReturnsVerificationEmailSentWithCode()
    {
        // Arrange
        var (service, handler, _) = CreateService(enabled: true);

        // Act
        await service.SendMfaCodeHtmlAsync("user@example.test", "123456");

        // Assert
        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("123456", body);
        Assert.Contains("html=", body);
    }

    [Fact]
    public async Task SendEmailAsync_MailgunEnabledNonSuccessResponse_ReturnsEnsureSuccessThrows()
    {
        // Arrange — enabled but endpoint rejects: the send attempt must be real, not swallowed
        var handler = new RejectingHttpMessageHandler();
        var service = new MailgunEmailService(
            new HttpClient(handler),
            CreateConfig(enabled: true),
            new Mock<ILogger<MailgunEmailService>>().Object);

        // Act + Assert — EnsureSuccessStatusCode surfaces as HttpRequestException
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SendEmailAsync("user@example.test", "S", "B"));
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }
}
