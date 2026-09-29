using System.ComponentModel.DataAnnotations;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Api.Contracts;

/// <summary>Evento financeiro recebido pela API.</summary>
public sealed record TransactionRequest
{
    /// <summary>Identificador único do evento; garante a idempotência.</summary>
    [Required]
    public Guid EventId { get; init; }

    [Required]
    public Guid AccountId { get; init; }

    /// <summary>CREDIT ou DEBIT.</summary>
    [Required]
    public TransactionType Type { get; init; }

    /// <summary>Valor positivo, com no máximo 2 casas decimais.</summary>
    [Range(typeof(decimal), "0.01", "9999999999999999", ParseLimitsInInvariantCulture = true)]
    public decimal Amount { get; init; }

    [Required]
    public DateTimeOffset OccurredAt { get; init; }

    public ProcessTransactionCommand ToCommand() => new(EventId, AccountId, Type, Amount, OccurredAt);
}
