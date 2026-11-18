using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Logging;

namespace StudentManagement.Tests;

public class ErrorLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NextThrows_ReturnsOriginalExceptionAfterLogging()
    {
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var context = CreateContext(errorLog);
        var original = new InvalidOperationException("Endpoint blew up");
        var middleware = new ErrorLoggingMiddleware(_ => throw original, NullLogger<ErrorLoggingMiddleware>.Instance);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, thrown);
        errorLog.Verify(s => s.LogAsync(
            "/Chat/Send",
            original,
            "user-1",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NextSucceeds_ReturnsWithoutLogging()
    {
        var errorLog = new Mock<IErrorLogService>();
        var context = CreateContext(errorLog);
        var middleware = new ErrorLoggingMiddleware(_ => Task.CompletedTask, NullLogger<ErrorLoggingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        errorLog.Verify(s => s.LogAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ClientDisconnect_ReturnsWithoutLogging()
    {
        var errorLog = new Mock<IErrorLogService>();
        var context = CreateContext(errorLog);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        context.RequestAborted = cts.Token;
        var middleware = new ErrorLoggingMiddleware(
            _ => throw new OperationCanceledException(cts.Token),
            NullLogger<ErrorLoggingMiddleware>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        errorLog.Verify(s => s.LogAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_LoggingFails_ReturnsOriginalException()
    {
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Error log table unreachable"));
        var context = CreateContext(errorLog);
        var original = new HttpRequestException("Provider down");
        var middleware = new ErrorLoggingMiddleware(_ => throw original, NullLogger<ErrorLoggingMiddleware>.Instance);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_ReturnsLogWithNullUserId()
    {
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var context = CreateContext(errorLog, authenticated: false);
        var middleware = new ErrorLoggingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<ErrorLoggingMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        errorLog.Verify(s => s.LogAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), null, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static DefaultHttpContext CreateContext(Mock<IErrorLogService> errorLog, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => errorLog.Object);

        var claims = new List<Claim>();
        if (authenticated)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        }

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "TestAuth" : null)),
            Request = { Path = "/Chat/Send" }
        };
    }
}