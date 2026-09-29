using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Api.ExceptionHandling;

/// <summary>Traduz exceções de domínio em respostas HTTP no formato ProblemDetails.</summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<DomainExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        var (status, title) = domainException switch
        {
            DuplicateEventException => (StatusCodes.Status409Conflict, "Evento duplicado"),
            InsufficientFundsException => (StatusCodes.Status422UnprocessableEntity, "Saldo insuficiente"),
            AccountNotFoundException => (StatusCodes.Status404NotFound, "Conta não encontrada"),
            InvalidTransactionException => (StatusCodes.Status400BadRequest, "Transação inválida"),
            _ => (StatusCodes.Status400BadRequest, "Requisição inválida")
        };

        // Regra de negócio recusando uma operação é comportamento esperado, não falha do sistema:
        // Information, com o tipo da recusa como propriedade para filtrar e contar no Elasticsearch.
        _logger.LogInformation(
            "Operação recusada ({Rejection:l}, HTTP {StatusCode}): {Reason:l}",
            domainException.GetType().Name, status, domainException.Message);

        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = domainException.Message
            }
        });
    }
}
