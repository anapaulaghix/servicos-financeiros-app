using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Produces("application/json")]
public sealed class AccountsController(IAccountQueries queries) : ControllerBase
{
    private readonly IAccountQueries _queries = queries;

    /// <summary>Lista as contas com o saldo atual consolidado.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AccountSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok(await _queries.ListAsync(cancellationToken));
    }

    /// <summary>Consulta uma conta.</summary>
    /// <response code="404">Conta não encontrada.</response>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType<AccountSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _queries.GetAsync(accountId, cancellationToken)
                      ?? throw new AccountNotFoundException(accountId);

        return Ok(account);
    }

    /// <summary>Extrato paginado, do lançamento mais recente para o mais antigo.</summary>
    /// <param name="accountId">Conta cujo extrato será consultado.</param>
    /// <param name="page">Página, começando em 1.</param>
    /// <param name="pageSize">Itens por página (1 a 100).</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="400">Parâmetros de paginação inválidos.</response>
    /// <response code="404">Conta não encontrada.</response>
    [HttpGet("{accountId:guid}/transactions")]
    [ProducesResponseType<PagedResult<StatementEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatement(
        Guid accountId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var statement = await _queries.GetStatementAsync(accountId, page, pageSize, cancellationToken)
                        ?? throw new AccountNotFoundException(accountId);

        return Ok(statement);
    }
}
