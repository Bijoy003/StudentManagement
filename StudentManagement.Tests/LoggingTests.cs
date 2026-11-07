using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StudentManagement.Logging;

namespace StudentManagement.Tests
{
    public class LogSanitizerTests
    {
        [Theory]
        [InlineData("password=hunter2", "hunter2")]
        [InlineData("api_key: sk-live-123", "sk-live-123")]
        [InlineData("Authorization: Bearer abc123", "abc123")]
        [InlineData("ConnectionString: Server=db;User Id=sa;Password=s3cret", "s3cret")]
        [InlineData("student jane@uni.edu enrolled", "jane@uni.edu")]
        public void Sanitize_RemovesSensitiveValue(string input, string secret)
        {
            var result = LogSanitizer.Sanitize(input);

            Assert.DoesNotContain(secret, result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Sanitize_KeepsLongNumericIdentifier()
        {
            const string input = "student 1234567890123456";

            Assert.Equal(input, LogSanitizer.Sanitize(input));
        }

        [Fact]
        public void Sanitize_LeavesOrdinaryTextUnchanged()
        {
            const string input = "Student 42 created by admin";

            Assert.Equal(input, LogSanitizer.Sanitize(input));
        }
    }

    public class CorrelationIdMiddlewareTests
    {
        private static async Task<HttpContext> InvokeAsync(string? correlationIdHeader, RequestDelegate? next = null)
        {
            var context = new DefaultHttpContext();

            if (correlationIdHeader is not null)
            {
                context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = correlationIdHeader;
            }

            var middleware = new CorrelationIdMiddleware(
                next ?? (_ => Task.CompletedTask),
                NullLogger<CorrelationIdMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            return context;
        }

        [Fact]
        public async Task InvokeAsync_WithWellFormedHeader_EchoesIt()
        {
            var context = await InvokeAsync("abc-123");

            var actual = context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString();

            Assert.Equal("abc-123", actual);
        }

        [Theory]
        [InlineData("bad\nvalue")]
        [InlineData("has space")]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
        public async Task InvokeAsync_WithMalformedHeader_ReplacesIt(string malformed)
        {
            var context = await InvokeAsync(malformed);

            var actual = context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString();

            Assert.NotEqual(malformed, actual);
            Assert.DoesNotContain("\n", actual);
            Assert.DoesNotContain(" ", actual);
            Assert.InRange(actual.Length, 1, 64);
        }

        [Fact]
        public async Task InvokeAsync_WhenNextThrows_Rethrows()
        {
            var middleware = new CorrelationIdMiddleware(
                _ => throw new InvalidOperationException("boom"),
                NullLogger<CorrelationIdMiddleware>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => middleware.InvokeAsync(new DefaultHttpContext()));
        }
    }
}
