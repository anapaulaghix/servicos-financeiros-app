using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using ServicosFinanceiros.Api.ExceptionHandling;
using ServicosFinanceiros.Api.Observability;
using ServicosFinanceiros.Api.RateLimiting;
using ServicosFinanceiros.Application;
using ServicosFinanceiros.Infrastructure;
using ServicosFinanceiros.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services
    .AddOptions<RateLimitPolicyOptions>(RateLimitPolicyOptions.Transactions)
    .Bind(builder.Configuration.GetSection($"{RateLimitPolicyOptions.SectionName}:{RateLimitPolicyOptions.Transactions}"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DatabaseInitializer.InitializeAsync(
        app.Services,
        seedDemoData: app.Configuration.GetValue<bool>("Database:SeedOnStartup"));
}

app.UseForwardedHeaders();
app.UseStructuredRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapControllers();

app.Run();

public partial class Program;
