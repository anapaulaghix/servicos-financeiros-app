using System.Globalization;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ServicosFinanceiros.Api.Observability;

public static class StructuredLogging
{
    /// <summary>
    /// Serilog como provedor de logs: eventos estruturados (propriedades, não só texto), enviados ao
    /// console e, quando `Elasticsearch:Url` estiver configurado, a um data stream do Elasticsearch.
    /// </summary>
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        var elasticsearchUrl = builder.Configuration["Elasticsearch:Url"];

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "ServicosFinanceiros.Api")
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

            // Em desenvolvimento, texto legível; em container, JSON (uma linha por evento) para coletores.
            if (builder.Environment.IsDevelopment())
            {
                logger.WriteTo.Console(
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }

            if (!string.IsNullOrWhiteSpace(elasticsearchUrl))
            {
                logger.WriteTo.Elasticsearch([new Uri(elasticsearchUrl)], options =>
                {
                    options.DataStream = new DataStreamName("logs", "servicos_financeiros", "api");
                    options.BootstrapMethod = BootstrapMethod.Silent;
                });
            }
        });

        return builder;
    }

    /// <summary>Uma linha de log por requisição HTTP, com método, rota, status e duração como propriedades.</summary>
    public static IApplicationBuilder UseStructuredRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnostics, context) =>
                diagnostics.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());
        });
    }
}
