using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Api.ExceptionHandling;

/// <summary>Traduz exceções de domínio em respostas HTTP no formato ProblemDetails.</summary>
public sealed partial class DomainExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        var (status, title, rejection) = domainException switch
        {
            DuplicateEventException => (StatusCodes.Status409Conflict, "Identificador de evento já utilizado", nameof(DuplicateEventException)),
            InsufficientFundsException => (StatusCodes.Status422UnprocessableEntity, "Saldo insuficiente", nameof(InsufficientFundsException)),
            AccountNotFoundException => (StatusCodes.Status404NotFound, "Conta não encontrada", nameof(AccountNotFoundException)),
            InvalidTransactionException => (StatusCodes.Status400BadRequest, "Transação inválida", nameof(InvalidTransactionException)),
            InvalidAccountException => (StatusCodes.Status400BadRequest, "Conta inválida", nameof(InvalidAccountException)),
            _ => (StatusCodes.Status400BadRequest, "Requisição inválida", nameof(DomainException))
        };

        // Regra de negócio recusando uma operação é comportamento esperado, não falha do sistema:
        // Information, com o tipo da recusa como propriedade para filtrar e contar no Elasticsearch.
        LogRejected(logger, rejection, status, domainException.Message);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Operação recusada ({Rejection:l}, HTTP {StatusCode}): {Reason:l}")]
    private static partial void LogRejected(ILogger logger, string rejection, int statusCode, string reason);
}
