using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.UnitTests.Application;

public class ProcessTransactionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 30, 10, 15, 0, TimeSpan.Zero);

    private readonly Mock<IAccountRepository> _accounts = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ProcessTransactionHandler _handler;

    public ProcessTransactionHandlerTests()
    {
        // O unit of work de teste apenas executa o trabalho; a transação real é testada na infraestrutura.
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Transaction>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Transaction>> work, CancellationToken ct) => work(ct));

        _handler = new ProcessTransactionHandler(
            _accounts.Object,
            _transactions.Object,
            _unitOfWork.Object,
            new FakeTimeProvider(Now),
            NullLogger<ProcessTransactionHandler>.Instance);
    }

    private Account GivenAccountWithBalance(decimal balance)
    {
        var account = Account.Open(Guid.NewGuid(), "Ana Paula", Now);
        if (balance > 0)
            account.Apply(Guid.NewGuid(), TransactionType.Credit, balance, Now, Now);

        _accounts
            .Setup(a => a.GetByIdForUpdateAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        return account;
    }

    private static ProcessTransactionCommand Command(Guid accountId, TransactionType type, decimal amount) =>
        new(Guid.NewGuid(), accountId, type, amount, Now);

    /// <summary>Lançamento já gravado para o eventId do comando, com os dados informados.</summary>
    private static Transaction StoredTransaction(ProcessTransactionCommand command, decimal amount)
    {
        var account = Account.Open(command.AccountId, "Ana Paula", Now);
        return account.Apply(command.EventId, command.Type, amount, command.OccurredAt, Now.AddMinutes(-5));
    }

    private void GivenStored(Transaction transaction) =>
        _transactions
            .Setup(t => t.FindAsync(transaction.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

    private void GivenConcurrentWinner(Transaction winner)
    {
        // Na primeira consulta o evento ainda não existe; ao gravar, o banco acusa a chave primária
        // porque outra requisição com o mesmo eventId gravou antes; a segunda consulta já o encontra.
        _transactions
            .SetupSequence(t => t.FindAsync(winner.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null)
            .ReturnsAsync(winner);
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Transaction>>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException(new InvalidOperationException("23505")));
    }

    [Fact]
    public async Task HandleAsync_ValidCredit_ShouldUpdateBalanceAndPersistTransaction()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Credit, 50m);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        account.Balance.Should().Be(150m);
        result.IsReplay.Should().BeFalse();
        result.Transaction.EventId.Should().Be(command.EventId);
        result.Transaction.ProcessedAt.Should().Be(Now);
        _transactions.Verify(t => t.Add(result.Transaction), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SameEventResent_ShouldReturnOriginalWithoutTouchingAccount()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Credit, 50m);
        var original = StoredTransaction(command, 50m);
        GivenStored(original);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsReplay.Should().BeTrue();
        result.Transaction.Should().BeSameAs(original);
        account.Balance.Should().Be(100m);
        _transactions.Verify(t => t.Add(It.IsAny<Transaction>()), Times.Never);
        _unitOfWork.Verify(u => u.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Transaction>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EventIdReusedWithDifferentData_ShouldThrowDuplicateAndNotTouchAccount()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Credit, 50m);
        GivenStored(StoredTransaction(command, amount: 70m));

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateEventException>())
            .Which.EventId.Should().Be(command.EventId);
        account.Balance.Should().Be(100m);
        _transactions.Verify(t => t.Add(It.IsAny<Transaction>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UniqueViolationOnSave_WithSameData_ShouldReturnTheWinnerAsReplay()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Credit, 50m);
        var winner = StoredTransaction(command, 50m);
        GivenConcurrentWinner(winner);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsReplay.Should().BeTrue();
        result.Transaction.Should().BeSameAs(winner);
    }

    [Fact]
    public async Task HandleAsync_UniqueViolationOnSave_WithDifferentData_ShouldThrowDuplicate()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Credit, 50m);
        GivenConcurrentWinner(StoredTransaction(command, amount: 70m));

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateEventException>())
            .Which.EventId.Should().Be(command.EventId);
    }

    [Fact]
    public async Task HandleAsync_InsufficientFunds_ShouldThrowAndNotPersist()
    {
        var account = GivenAccountWithBalance(100m);
        var command = Command(account.Id, TransactionType.Debit, 100.01m);

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        account.Balance.Should().Be(100m);
        _transactions.Verify(t => t.Add(It.IsAny<Transaction>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnknownAccount_ShouldThrowAccountNotFound()
    {
        var command = Command(Guid.NewGuid(), TransactionType.Credit, 10m);
        _accounts
            .Setup(a => a.GetByIdForUpdateAsync(command.AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<AccountNotFoundException>();
        _transactions.Verify(t => t.Add(It.IsAny<Transaction>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldRunInsideUnitOfWork()
    {
        var account = GivenAccountWithBalance(0m);

        await _handler.HandleAsync(Command(account.Id, TransactionType.Credit, 10m), CancellationToken.None);

        _unitOfWork.Verify(u => u.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Transaction>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
