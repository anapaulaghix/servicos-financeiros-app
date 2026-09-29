using Microsoft.AspNetCore.Mvc;
using ServicosFinanceiros.Api.Contracts;
using ServicosFinanceiros.Api.RateLimiting;
using ServicosFinanceiros.Application.Transactions;

namespace ServicosFinanceiros.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Produces("application/json")]
public sealed class TransactionsController : ControllerBase
{
    private readonly IProcessTransactionHandler _handler;

    public TransactionsController(IProcessTransactionHandler handler)
    {
        _handler = handler;
    }

    /// <summary>Processa um evento de crédito ou débito em uma conta.</summary>
    /// <response code="201">Evento processado; retorna o lançamento e o saldo resultante.</response>
    /// <response code="400">Payload inválido.</response>
    /// <response code="404">Conta não encontrada.</response>
    /// <response code="409">Evento já processado (eventId duplicado).</response>
    /// <response code="422">Saldo insuficiente para o débito.</response>
    /// <response code="429">Limite de requisições excedido; aguarde o tempo do cabeçalho Retry-After.</response>
    [HttpPost]
    [RateLimit(RateLimitPolicyOptions.Transactions)]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Post(TransactionRequest request, CancellationToken cancellationToken)
    {
        var transaction = await _handler.HandleAsync(request.ToCommand(), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, TransactionResponse.From(transaction));
    }
}
