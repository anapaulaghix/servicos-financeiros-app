using FluentAssertions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.UnitTests.Domain;

public class TransactionTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 30, 10, 15, 0, TimeSpan.Zero);

    private static Transaction Stored()
    {
        var account = Account.Open(Guid.NewGuid(), "Ana Paula", OccurredAt);
        return account.Apply(Guid.NewGuid(), TransactionType.Credit, 150.75m, OccurredAt, OccurredAt);
    }

    [Fact]
    public void IsSameEvent_WithIdenticalData_ShouldBeTrue()
    {
        var transaction = Stored();

        transaction.IsSameEvent(transaction.AccountId, TransactionType.Credit, 150.75m, OccurredAt)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSameEvent_SameInstantInAnotherTimeZoneOrScale_ShouldBeTrue()
    {
        // O cliente pode reenviar o mesmo instante com outro fuso, e o valor com outra escala (150.750).
        var transaction = Stored();
        var sameInstant = OccurredAt.ToOffset(TimeSpan.FromHours(-3));

        transaction.IsSameEvent(transaction.AccountId, TransactionType.Credit, 150.750m, sameInstant)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSameEvent_DifferenceBelowOneMicrosecond_ShouldBeTrue()
    {
        // O banco guarda microssegundos: o que o PostgreSQL descarta não pode diferenciar um reenvio.
        var transaction = Stored();

        transaction.IsSameEvent(transaction.AccountId, TransactionType.Credit, 150.75m, OccurredAt.AddTicks(5))
            .Should().BeTrue();
    }

    [Fact]
    public void IsSameEvent_WithAnyDifferentField_ShouldBeFalse()
    {
        var transaction = Stored();

        transaction.IsSameEvent(Guid.NewGuid(), TransactionType.Credit, 150.75m, OccurredAt).Should().BeFalse();
        transaction.IsSameEvent(transaction.AccountId, TransactionType.Debit, 150.75m, OccurredAt).Should().BeFalse();
        transaction.IsSameEvent(transaction.AccountId, TransactionType.Credit, 150.76m, OccurredAt).Should().BeFalse();
        transaction.IsSameEvent(transaction.AccountId, TransactionType.Credit, 150.75m, OccurredAt.AddSeconds(1))
            .Should().BeFalse();
    }
}
