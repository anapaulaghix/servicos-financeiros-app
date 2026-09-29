using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_transactions_balance_after_non_negative", "balance_after >= 0");
        });

        // A chave primária é o eventId: o banco garante que um evento nunca é gravado duas vezes.
        builder.HasKey(t => t.EventId);
        builder.Property(t => t.EventId).HasColumnName("event_id").ValueGeneratedNever();

        builder.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(t => t.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnName("amount").HasPrecision(18, Account.MoneyScale).IsRequired();
        builder.Property(t => t.BalanceAfter).HasColumnName("balance_after").HasPrecision(18, Account.MoneyScale).IsRequired();
        builder.Property(t => t.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(t => t.ProcessedAt).HasColumnName("processed_at").IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sustenta o extrato paginado: lançamentos de uma conta, do mais recente para o mais antigo,
        // na ordem de processamento (a ordem em que o saldo realmente mudou).
        builder.HasIndex(t => new { t.AccountId, t.ProcessedAt })
            .IsDescending(false, true);

        builder.Ignore(t => t.SignedAmount);
    }
}
