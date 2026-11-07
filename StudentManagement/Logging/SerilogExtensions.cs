using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Diagnostics;

namespace StudentManagement.Logging
{
    public static class SerilogExtensions
    {
        public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
        {
            var configuration = builder.Configuration;

            // Sinks are declared here so the log directory resolves against the content root: under IIS
            // the current working directory (%WINDIR%\System32\inetsrv) is not writable, which makes a
            // relative "logs/" path fail silently. Levels and enrichers stay in the "Serilog" section of
            // appsettings.json - declaring the same sink in both places writes every event twice.
            var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.With<CorrelationIdEnricher>()
                .Enrich.With<SensitiveDataEnricher>()
                .WriteTo.Console(new JsonFormatter())
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "studentmanagement-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    formatter: new JsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "studentmanagement-errors-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    formatter: new JsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Error)
                .CreateLogger();

            builder.Host.UseSerilog();

            return builder;
        }

        public static WebApplication UseSerilogLogging(this WebApplication app)
        {
            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
                options.GetLevel = (_, _, exception) =>
                    exception is null ? LogEventLevel.Information : LogEventLevel.Error;
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
                    diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                    diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
                    diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
                    diagnosticContext.Set("UserName", httpContext.User?.Identity?.Name ?? "anonymous");
                    diagnosticContext.Set(
                        "CorrelationId",
                        httpContext.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].FirstOrDefault()
                            ?? string.Empty);
                };
            });

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseHttpMetrics();

            // /metrics has no authorization, so it is only published in development. In production,
            // scrape it over an internal port or front it with a network policy instead.
            if (app.Environment.IsDevelopment())
            {
                app.MapMetrics();
            }

            return app;
        }
    }

    /// <summary>
    /// Adds a correlation id to every event, including events logged outside a request (startup,
    /// background work). A request-scoped CorrelationId pushed by <see cref="CorrelationIdMiddleware"/>
    /// wins, because <c>AddPropertyIfAbsent</c> keeps the value that is already present.
    /// </summary>
    public class CorrelationIdEnricher : ILogEventEnricher
    {
        private const string CorrelationIdProperty = "CorrelationId";

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var traceId = Activity.Current?.TraceId.ToString();

            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(CorrelationIdProperty, traceId));
        }
    }

    /// <summary>
    /// Redacts credentials and personal data from the string properties of every event.
    /// </summary>
    public class SensitiveDataEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // Snapshot first: AddOrUpdateProperty mutates the collection we would otherwise enumerate.
            foreach (var (name, value) in logEvent.Properties.ToArray())
            {
                if (value is not ScalarValue { Value: string text })
                {
                    continue;
                }

                var sanitized = LogSanitizer.Sanitize(text);
                if (!string.Equals(sanitized, text, StringComparison.Ordinal))
                {
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, sanitized));
                }
            }
        }
    }

    public static class OpenTelemetryExtensions
    {
        public static WebApplicationBuilder AddOpenTelemetry(this WebApplicationBuilder builder)
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("StudentManagement"))
                .WithTracing(tracing =>
                {
                    // No custom ActivitySource is emitted today; add .AddSource(...) alongside one if
                    // application-level spans are introduced.
                    tracing
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = httpContext =>
                                !httpContext.Request.Path.StartsWithSegments("/metrics");
                        })
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation(options =>
                        {
                            options.SetDbStatementForText = true;
                            options.SetDbStatementForStoredProcedure = true;
                        });

                    // Exporting every span to the console duplicates the Serilog console sink and, in
                    // production, dumps one line per HTTP and EF Core call to stdout.
                    if (builder.Environment.IsDevelopment())
                    {
                        tracing.AddConsoleExporter();
                    }
                })
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation());

            return builder;
        }
    }
}
