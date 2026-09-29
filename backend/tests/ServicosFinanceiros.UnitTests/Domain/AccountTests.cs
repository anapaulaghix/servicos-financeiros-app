using FluentAssertions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.UnitTests.Domain;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 30, 10, 15, 0, TimeSpan.Zero);

    private static Account NewAccount() => Account.Open(Guid.NewGuid(), "Ana Paula", Now);

    private static Account AccountWithBalance(decimal balance)
    {
        var account = NewAccount();
        if (balance > 0)
            account.Apply(Guid.NewGuid(), TransactionType.Credit, balance, Now, Now);
        return account;
    }

    [Fact]
    public void Open_ShouldStartWithZeroBalance()
    {
        var account = NewAccount();

        account.Balance.Should().Be(0m);
        account.HolderName.Should().Be("Ana Paula");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_WithoutHolderName_ShouldThrow(string holderName)
    {
        var act = () => Account.Open(Guid.NewGuid(), holderName, Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Open_WithEmptyId_ShouldThrow()
    {
        var act = () => Account.Open(Guid.Empty, "Ana Paula", Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Apply_Credit_ShouldIncreaseBalanceAndRecordTransaction()
    {
        var account = AccountWithBalance(100m);
        var eventId = Guid.NewGuid();

        var transaction = account.Apply(eventId, TransactionType.Credit, 150.75m, Now, Now);

        account.Balance.Should().Be(250.75m);
        transaction.EventId.Should().Be(eventId);
        transaction.AccountId.Should().Be(account.Id);
        transaction.Type.Should().Be(TransactionType.Credit);
        transaction.Amount.Should().Be(150.75m);
        transaction.BalanceAfter.Should().Be(250.75m);
        transaction.SignedAmount.Should().Be(150.75m);
    }

    [Fact]
    public void Apply_Debit_ShouldDecreaseBalanceAndRecordNegativeImpact()
    {
        var account = AccountWithBalance(100m);

        var transaction = account.Apply(Guid.NewGuid(), TransactionType.Debit, 40.25m, Now, Now);

        account.Balance.Should().Be(59.75m);
        transaction.BalanceAfter.Should().Be(59.75m);
        transaction.SignedAmount.Should().Be(-40.25m);
    }

    [Fact]
    public void Apply_DebitEqualToBalance_ShouldLeaveBalanceAtZero()
    {
        var account = AccountWithBalance(100m);

        account.Apply(Guid.NewGuid(), TransactionType.Debit, 100m, Now, Now);

        account.Balance.Should().Be(0m);
    }

    [Fact]
    public void Apply_DebitGreaterThanBalance_ShouldThrowAndKeepBalance()
    {
        var account = AccountWithBalance(100m);

        var act = () => account.Apply(Guid.NewGuid(), TransactionType.Debit, 100.01m, Now, Now);

        act.Should().Throw<InsufficientFundsException>()
            .Which.Should().Match<InsufficientFundsException>(e =>
                e.AccountId == account.Id && e.Balance == 100m && e.RequestedAmount == 100.01m);
        account.Balance.Should().Be(100m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Apply_NonPositiveAmount_ShouldThrow(decimal amount)
    {
        var account = AccountWithBalance(100m);

        var act = () => account.Apply(Guid.NewGuid(), TransactionType.Credit, amount, Now, Now);

        act.Should().Throw<InvalidTransactionException>();
        account.Balance.Should().Be(100m);
    }

    [Fact]
    public void Apply_AmountWithMoreThanTwoDecimals_ShouldThrow()
    {
        var account = NewAccount();

        var act = () => account.Apply(Guid.NewGuid(), TransactionType.Credit, 10.999m, Now, Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Apply_EmptyEventId_ShouldThrow()
    {
        var account = NewAccount();

        var act = () => account.Apply(Guid.Empty, TransactionType.Credit, 10m, Now, Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Apply_UndefinedType_ShouldThrow()
    {
        var account = NewAccount();

        var act = () => account.Apply(Guid.NewGuid(), (TransactionType)99, 10m, Now, Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Apply_Sequence_ShouldKeepBalanceEqualToSumOfSignedAmounts()
    {
        var account = NewAccount();
        var transactions = new[]
        {
            account.Apply(Guid.NewGuid(), TransactionType.Credit, 500m, Now, Now),
            account.Apply(Guid.NewGuid(), TransactionType.Debit, 120.50m, Now, Now),
            account.Apply(Guid.NewGuid(), TransactionType.Credit, 0.10m, Now, Now),
            account.Apply(Guid.NewGuid(), TransactionType.Debit, 79.60m, Now, Now)
        };

        account.Balance.Should().Be(transactions.Sum(t => t.SignedAmount));
        transactions.Select(t => t.BalanceAfter).Should().Equal(500m, 379.50m, 379.60m, 300m);
    }
}
