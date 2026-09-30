using System.Net;
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
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ValidationProblemFactory.Create)
    .AddJsonOptions(options =>
    {
        // "CREDIT"/"DEBIT" apenas: um número (ex.: 2) não pode virar um tipo de lançamento.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        // Mensagens do desserializador expõem nomes de tipos internos; o 400 usa textos próprios.
        options.AllowInputFormatterExceptionMessages = false;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services
    .AddOptions<RateLimitPolicyOptions>(RateLimitPolicyOptions.Transactions)
    .Bind(builder.Configuration.GetSection($"{RateLimitPolicyOptions.SectionName}:{RateLimitPolicyOptions.Transactions}"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// A API fica atrás do nginx: o IP do cliente chega em X-Forwarded-For. Só o IP do proxy configurado
// (ReverseProxy:TrustedProxies; no Docker, o IP fixo do nginx) pode informá-lo. Quem chama a API
// direto não consegue forjar o próprio IP para burlar o rate limiting.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Comentários XML dos controllers e contratos viram descrições e exemplos no Swagger.
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "ServicosFinanceiros.Api.xml"));
});

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
