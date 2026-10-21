using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Enrichers;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StudentManagement.Logging
{
    public static class SerilogExtensions
    {
        public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
        {
            var configuration = builder.Configuration;

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "StudentManagement")
                .Enrich.With<CorrelationIdEnricher>()
                .Enrich.With<SensitiveDataEnricher>()
                .WriteTo.Console(new JsonFormatter())
                .WriteTo.File(
                    path: "logs/studentmanagement-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    formatter: new JsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(
                    path: "logs/studentmanagement-errors-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    formatter: new JsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Error)
                .CreateLogger();

            builder.Host.UseSerilog();
            builder.Services.AddSingleton<IMetricCollector, PrometheusMetricCollector>();
            builder.Services.AddSingleton<ITracingService, OpenTelemetryTracingService>();

            return builder;
        }

        public static WebApplication UseSerilogLogging(this WebApplication app)
        {
            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                    diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                    diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault());
                    diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString());
                    diagnosticContext.Set("CorrelationId", httpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault());
                };
            });

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseHttpMetrics();
            app.MapMetrics();

            return app;
        }
    }

    public class CorrelationIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (LogContext.PushProperty("CorrelationId", Activity.Current?.Id ?? "unknown") is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public class SensitiveDataEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            foreach (var property in logEvent.Properties)
            {
                if (property.Value is ScalarValue scalar && scalar.Value is string str)
                {
                    var sanitized = LogSanitizer.Sanitize(str);
                    if (sanitized != str)
                    {
                        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(property.Key, sanitized));
                    }
                }
            }
        }
    }

    public interface IMetricCollector
    {
        void IncrementCounter(string name, string help, params string[] labels);
        void RecordHistogram(string name, string help, double value, params string[] labels);
        void SetGauge(string name, string help, double value, params string[] labels);
    }

    public class PrometheusMetricCollector : IMetricCollector
    {
        private readonly Counter _requestCounter;
        private readonly Histogram _requestDuration;
        private readonly Gauge _activeRequests;

        public PrometheusMetricCollector()
        {
            _requestCounter = Metrics.CreateCounter("http_requests_total", "Total HTTP requests", "method", "path", "status_code");
            _requestDuration = Metrics.CreateHistogram("http_request_duration_seconds", "HTTP request duration in seconds", "method", "path");
            _activeRequests = Metrics.CreateGauge("http_active_requests", "Active HTTP requests", "method", "path");
        }

        public void IncrementCounter(string name, string help, params string[] labels)
        {
            var counter = Metrics.CreateCounter(name, help, labels);
            counter.Inc();
        }

        public void RecordHistogram(string name, string help, double value, params string[] labels)
        {
            var histogram = Metrics.CreateHistogram(name, help, labels);
            histogram.Observe(value);
        }

        public void SetGauge(string name, string help, double value, params string[] labels)
        {
            var gauge = Metrics.CreateGauge(name, help, labels);
            gauge.Set(value);
        }
    }

    public interface ITracingService
    {
        Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);
    }

    public class OpenTelemetryTracingService : ITracingService
    {
        private static readonly ActivitySource ActivitySource = new("StudentManagement");

        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            return ActivitySource.StartActivity(name, kind);
        }
    }

    public static class OpenTelemetryExtensions
    {
        public static WebApplicationBuilder AddOpenTelemetry(this WebApplicationBuilder builder)
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("StudentManagement"))
                .WithTracing(tracing => tracing
                    .AddSource("StudentManagement")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = (httpContext) => !httpContext.Request.Path.StartsWithSegments("/metrics");
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                    })
                    .AddConsoleExporter())
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation());

            return builder;
        }
    }
}