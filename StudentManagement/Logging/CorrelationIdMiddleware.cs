using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace StudentManagement.Logging
{
    public class CorrelationIdMiddleware
    {
        public const string CorrelationIdHeader = "X-Correlation-ID";

        private const string CorrelationIdLogProperty = "CorrelationId";
        private const int MaxCorrelationIdLength = 64;

        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = ResolveCorrelationId(context);

            context.Response.Headers[CorrelationIdHeader] = correlationId;

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                [CorrelationIdLogProperty] = correlationId,
                ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
                ["RequestMethod"] = context.Request.Method,
                ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
                ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
            });

            // The access-log line itself comes from UseSerilogRequestLogging. These two are kept at
            // Debug so a request is not logged three times at the default Information level.
            _logger.LogDebug("HTTP {Method} {Path} started", context.Request.Method, context.Request.Path);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _next(context);
                stopwatch.Stop();

                _logger.LogDebug(
                    "HTTP {Method} {Path} completed in {ElapsedMs}ms with status {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds,
                    context.Response.StatusCode);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();

                // The status code is still the default here: UseExceptionHandler sits outside this
                // middleware and only converts the response after we unwind. Log the failure instead of
                // reporting a misleading "completed with status 200".
                _logger.LogError(
                    exception,
                    "HTTP {Method} {Path} failed after {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }

        /// <summary>
        /// Uses a well-formed client supplied correlation id, otherwise falls back to the trace id so
        /// the value can be joined to the OpenTelemetry spans, and finally to the ASP.NET request id.
        /// </summary>
        private static string ResolveCorrelationId(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerValues))
            {
                var candidate = headerValues.ToString().Trim();
                if (IsWellFormed(candidate))
                {
                    return candidate;
                }
            }

            return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        }

        /// <summary>
        /// Rejects control characters and whitespace so a client cannot forge log lines, and caps the
        /// length so an oversized header cannot inflate every stored event.
        /// </summary>
        private static bool IsWellFormed(string value)
        {
            if (value.Length == 0 || value.Length > MaxCorrelationIdLength)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
