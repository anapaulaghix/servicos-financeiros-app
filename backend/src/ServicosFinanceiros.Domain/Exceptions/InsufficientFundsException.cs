using System.Globalization;

namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class InsufficientFundsException(Guid accountId, decimal balance, decimal requestedAmount)
    : DomainException($"Saldo insuficiente na conta {accountId}: saldo R$ {Money(balance)}, débito solicitado R$ {Money(requestedAmount)}.")
{
    // Formato brasileiro fixo (1.234,56), independente da cultura do processo: no container ela é
    // invariante e a mensagem sairia "1234.56". Montado à mão para não depender dos dados de cultura
    // (ICU), que podem não existir na imagem.
    private static readonly NumberFormatInfo BrazilianMoney = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2,
    };

    public Guid AccountId { get; } = accountId;
    public decimal Balance { get; } = balance;
    public decimal RequestedAmount { get; } = requestedAmount;

    private static string Money(decimal value) => value.ToString("N2", BrazilianMoney);
}
