using System.Text.Json;
using System.Text.Json.Serialization;
using ServicosFinanceiros.Api.ExceptionHandling;
using ServicosFinanceiros.Application;
using ServicosFinanceiros.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composição de dependências: cada camada expõe seu próprio registro.
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
