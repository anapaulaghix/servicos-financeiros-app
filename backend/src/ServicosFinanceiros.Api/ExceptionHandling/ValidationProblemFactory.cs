using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ServicosFinanceiros.Api.ExceptionHandling;

/// <summary>
/// Resposta 400 para payloads inválidos, em português e sem expor detalhes internos.
/// Por padrão o ASP.NET devolve mensagens em inglês do desserializador JSON, que incluem nomes de
/// tipos internos (ex.: "could not be converted to ServicosFinanceiros.Domain.Accounts.TransactionType").
/// </summary>
public static class ValidationProblemFactory
{
    public const string Title = "Dados inválidos";
    private const string InvalidFormatMessage = "Valor em formato inválido.";
    private const string MissingBodyMessage = "O corpo da requisição está ausente ou não é um JSON válido.";

    public static IActionResult Create(ActionContext context)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.ValidationState != ModelValidationState.Invalid || entry.Errors.Count == 0)
                continue;

            var field = NormalizeKey(key);
            errors[field] = entry.Errors
                .Select(error => Translate(field, error))
                .Distinct()
                .ToArray();
        }

        // Um campo com formato errado faz o ASP.NET também acusar o corpo inteiro ("request"); se há
        // erro em algum campo específico, só ele é mostrado, para não sugerir que o corpo está ausente.
        if (errors.Keys.Any(key => !IsBodyKey(key)))
        {
            foreach (var key in errors.Keys.Where(IsBodyKey).ToList())
                errors.Remove(key);
        }

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = Title,
            Detail = "Revise os campos indicados em 'errors'.",
        };

        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" },
        };
    }

    private static bool IsBodyKey(string field) => field is "request" or "$" or "";

    // "$.type" (erro do desserializador) e "Type" (erro de validação) viram "type", como no contrato JSON.
    private static string NormalizeKey(string key)
    {
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return field.Length == 0 ? field : char.ToLowerInvariant(field[0]) + field[1..];
    }

    private static string Translate(string field, ModelError error)
    {
        // Corpo ausente ou JSON quebrado: o erro chega no parâmetro da action ("request") ou na raiz ("$").
        if (IsBodyKey(field))
            return MissingBodyMessage;

        // Erros do desserializador (tipo errado, valor fora do enum) trazem a exceção ou a mensagem
        // genérica em inglês; as mensagens das validações do contrato já estão em português.
        if (error.Exception is not null || string.IsNullOrWhiteSpace(error.ErrorMessage) ||
            error.ErrorMessage.StartsWith("The ", StringComparison.Ordinal))
            return InvalidFormatMessage;

        return error.ErrorMessage;
    }
}
