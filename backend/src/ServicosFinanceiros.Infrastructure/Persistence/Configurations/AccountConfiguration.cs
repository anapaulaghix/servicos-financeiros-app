using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", table =>
            table.HasCheckConstraint("ck_accounts_balance_non_negative", "balance >= 0"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.HolderName).HasColumnName("holder_name").HasMaxLength(200).IsRequired();
        builder.Property(a => a.Balance).HasColumnName("balance").HasPrecision(18, Account.MoneyScale).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
