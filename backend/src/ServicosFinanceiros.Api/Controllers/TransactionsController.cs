using Microsoft.AspNetCore.Mvc;
using ServicosFinanceiros.Api.Contracts;
using ServicosFinanceiros.Api.RateLimiting;
using ServicosFinanceiros.Application.Transactions;

namespace ServicosFinanceiros.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Produces("application/json")]
public sealed class TransactionsController(IProcessTransactionHandler handler, ITransactionQueries queries) : ControllerBase
{
    /// <summary>Presente na resposta 200 de um reenvio: o lançamento devolvido já existia.</summary>
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    /// <summary>Processa um evento de crédito ou débito em uma conta.</summary>
    /// <remarks>
    /// O <c>eventId</c> é a chave de idempotência: o valor nunca é lançado duas vezes. Reenviar o mesmo evento
    /// (ex.: depois de um timeout) devolve 200 com o lançamento original e o cabeçalho
    /// <c>Idempotent-Replayed: true</c>; reutilizar o <c>eventId</c> com outros dados devolve 409.
    /// A data pode vir em qualquer fuso e é armazenada em UTC.
    /// </remarks>
    /// <response code="201">Evento processado; retorna o lançamento e o saldo resultante. O cabeçalho Location aponta para o lançamento.</response>
    /// <response code="200">Reenvio de um evento já processado com os mesmos dados; retorna o lançamento original, sem lançar de novo.</response>
    /// <response code="400">Payload inválido (campo ausente, formato ou valor fora do permitido).</response>
    /// <response code="404">Conta não encontrada.</response>
    /// <response code="409">O eventId já foi usado num lançamento com outros dados.</response>
    /// <response code="422">Saldo insuficiente para o débito.</response>
    /// <response code="429">Limite de requisições excedido; aguarde o tempo do cabeçalho Retry-After.</response>
    [HttpPost]
    [RateLimit(RateLimitPolicyOptions.Transactions)]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Post(TransactionRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        var response = TransactionResponse.From(result.Transaction);

        if (result.IsReplay)
        {
            // Nada foi criado agora: 200 com o lançamento original, sinalizado para o cliente distinguir.
            Response.Headers[IdempotentReplayedHeader] = "true";
            return Ok(response);
        }

        return CreatedAtAction(nameof(Get), new { eventId = response.EventId }, response);
    }

    /// <summary>Consulta um lançamento pelo identificador do evento.</summary>
    /// <response code="200">Lançamento encontrado.</response>
    /// <response code="404">Nenhum lançamento com esse eventId.</response>
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid eventId, CancellationToken cancellationToken)
    {
        var transaction = await queries.GetAsync(eventId, cancellationToken);

        return transaction is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Lançamento não encontrado",
                detail: $"Nenhum lançamento com o evento {eventId}.")
            : Ok(TransactionResponse.From(transaction));
    }
}
