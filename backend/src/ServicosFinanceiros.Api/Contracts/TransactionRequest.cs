using System.ComponentModel.DataAnnotations;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Api.Contracts;

/// <summary>Evento financeiro recebido pela API.</summary>
/// <remarks>
/// Os campos são anuláveis de propósito: em tipos de valor (Guid, decimal, DateTimeOffset, enum) o
/// <c>[Required]</c> não enxerga um campo ausente, que viraria o valor padrão em silêncio (ex.: data
/// 0001-01-01). Anuláveis, a ausência é detectada e a API responde 400 antes de tocar no banco.
/// </remarks>
public sealed record TransactionRequest
{
    /// <summary>Identificador único do evento (UUID); garante a idempotência.</summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [Required(ErrorMessage = "O identificador do evento é obrigatório.")]
    public Guid? EventId { get; init; }

    /// <summary>Conta que recebe o lançamento.</summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    [Required(ErrorMessage = "A conta é obrigatória.")]
    public Guid? AccountId { get; init; }

    /// <summary>CREDIT ou DEBIT.</summary>
    /// <example>CREDIT</example>
    [Required(ErrorMessage = "O tipo é obrigatório (CREDIT ou DEBIT).")]
    public TransactionType? Type { get; init; }

    /// <summary>Valor positivo, com no máximo 2 casas decimais.</summary>
    /// <example>150.75</example>
    [Required(ErrorMessage = "O valor é obrigatório.")]
    [Range(typeof(decimal), "0.01", "9999999999999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "O valor deve estar entre 0,01 e 9.999.999.999.999.999.")]
    public decimal? Amount { get; init; }

    /// <summary>Quando o evento ocorreu (ISO-8601, com qualquer fuso; é armazenado em UTC).</summary>
    /// <example>2026-01-30T10:15:00Z</example>
    [Required(ErrorMessage = "A data de ocorrência é obrigatória.")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>Converte para o comando. Só é chamado depois da validação do modelo (campos presentes).</summary>
    public ProcessTransactionCommand ToCommand() => new(
        EventId!.Value,
        AccountId!.Value,
        Type!.Value,
        Amount!.Value,
        // O PostgreSQL (timestamptz via Npgsql) só aceita offset zero; o instante é o mesmo.
        OccurredAt!.Value.ToUniversalTime());
}
