using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>
/// Cenários críticos do enunciado exercitados contra um PostgreSQL real:
/// idempotência, consistência do saldo, transacionalidade e concorrência.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ProcessTransactionIntegrationTests(PostgresFixture fixture)
{
    // Data fixa: reenvios do mesmo evento precisam chegar com dados idênticos.
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 30, 10, 15, 0, TimeSpan.Zero);

    /// <summary>Executa um evento em um escopo próprio, como faria uma requisição HTTP independente.</summary>
    private async Task<ProcessTransactionResult> ProcessAsync(Guid accountId, TransactionType type, decimal amount, Guid? eventId = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessTransactionHandler>();

        return await handler.HandleAsync(
            new ProcessTransactionCommand(eventId ?? Guid.NewGuid(), accountId, type, amount, OccurredAt),
            CancellationToken.None);
    }

    [Fact]
    public async Task Credit_ShouldPersistTransactionAndBalanceTogether()
    {
        var accountId = await fixture.CreateAccountAsync(100m);

        var result = (await ProcessAsync(accountId, TransactionType.Credit, 50.25m)).Transaction;

        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(150.25m);
        result.BalanceAfter.Should().Be(150.25m);
        transactions.Should().HaveCount(2); // crédito inicial + o novo
        transactions.Single(t => t.EventId == result.EventId).BalanceAfter.Should().Be(150.25m);
    }

    [Fact]
    public async Task SameEventSentTwice_ShouldReturnTheOriginalAndCountTheBalanceOnce()
    {
        var accountId = await fixture.CreateAccountAsync(100m);
        var eventId = Guid.NewGuid();

        var first = await ProcessAsync(accountId, TransactionType.Credit, 40m, eventId);
        var second = await ProcessAsync(accountId, TransactionType.Credit, 40m, eventId);

        first.IsReplay.Should().BeFalse();
        second.IsReplay.Should().BeTrue();
        second.Transaction.BalanceAfter.Should().Be(140m);
        second.Transaction.ProcessedAt.Should().Be(first.Transaction.ProcessedAt);
        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(140m);
        transactions.Count(t => t.EventId == eventId).Should().Be(1);
    }

    [Fact]
    public async Task SameEventIdWithDifferentData_ShouldBeRejectedAndLeaveTheBalanceUntouched()
    {
        var accountId = await fixture.CreateAccountAsync(100m);
        var eventId = Guid.NewGuid();
        await ProcessAsync(accountId, TransactionType.Credit, 40m, eventId);

        var reused = () => ProcessAsync(accountId, TransactionType.Debit, 40m, eventId);

        await reused.Should().ThrowAsync<DuplicateEventException>();
        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(140m);
        transactions.Count(t => t.EventId == eventId).Should().Be(1);
    }

    [Fact]
    public async Task DebitAboveBalance_ShouldBeRejectedAndLeaveNoTrace()
    {
        var accountId = await fixture.CreateAccountAsync(100m);

        var act = () => ProcessAsync(accountId, TransactionType.Debit, 100.01m);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(100m);
        transactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnknownAccount_ShouldThrowAccountNotFound()
    {
        var act = () => ProcessAsync(Guid.NewGuid(), TransactionType.Credit, 10m);

        await act.Should().ThrowAsync<AccountNotFoundException>();
    }

    [Fact]
    public async Task IdenticalEventsInParallel_ShouldProcessExactlyOnceAndAnswerAllWithTheSameResult()
    {
        // Simula um cliente que reenvia o mesmo evento várias vezes ao mesmo tempo (retry agressivo).
        // Todas as requisições recebem o lançamento; só uma o grava, as demais recebem o reenvio.
        var accountId = await fixture.CreateAccountAsync(0m);
        var eventId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => ProcessAsync(accountId, TransactionType.Credit, 25m, eventId)));

        outcomes.Count(outcome => !outcome.IsReplay).Should().Be(1);
        outcomes.Should().OnlyContain(outcome => outcome.Transaction.EventId == eventId && outcome.Transaction.BalanceAfter == 25m);
        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(25m);
        transactions.Should().ContainSingle(t => t.EventId == eventId);
    }

    [Fact]
    public async Task ConcurrentDebits_ShouldNeverLetBalanceGoNegative()
    {
        // 10 débitos de 20 disputando uma conta de 100: só 5 cabem. Sem bloqueio da linha,
        // todos leriam saldo 100 e passariam.
        var accountId = await fixture.CreateAccountAsync(100m);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try
            {
                await ProcessAsync(accountId, TransactionType.Debit, 20m);
                return true;
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        }));

        outcomes.Count(succeeded => succeeded).Should().Be(5);
        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(0m);
        transactions.Where(t => t.Type == TransactionType.Debit).Should().HaveCount(5);
    }

    [Fact]
    public async Task BalanceShouldEqualSumOfTransactionHistoryAfterConcurrentActivity()
    {
        var accountId = await fixture.CreateAccountAsync(200m);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                var type = i % 2 == 0 ? TransactionType.Credit : TransactionType.Debit;
                await ProcessAsync(accountId, type, 15m + i);
            }
            catch (InsufficientFundsException)
            {
                // Esperado em alguns débitos; o que importa é a consistência final.
            }
        }));

        var (balance, transactions) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(transactions.Sum(t => t.SignedAmount));
        balance.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task FailureWhileSaving_ShouldRollBackBalanceUpdate()
    {
        // Prova a atomicidade: o saldo é alterado e o lançamento é inserido no mesmo SaveChanges,
        // mas o eventId já existe. O banco rejeita o insert e o UPDATE do saldo precisa ser desfeito.
        var accountId = await fixture.CreateAccountAsync(100m);
        var existing = (await ProcessAsync(accountId, TransactionType.Credit, 10m)).Transaction; // saldo 110

        await using var scope = fixture.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var account = (await accounts.GetByIdForUpdateAsync(accountId, ct))!;
            var duplicate = account.Apply(existing.EventId, TransactionType.Credit, 500m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            transactions.Add(duplicate); // mesmo eventId: viola a chave primária
            return duplicate;
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UniqueConstraintViolationException>();
        var (balance, history) = await fixture.ReadAccountAsync(accountId);
        balance.Should().Be(110m); // os +500 não foram gravados
        history.Should().HaveCount(2);
    }
}
