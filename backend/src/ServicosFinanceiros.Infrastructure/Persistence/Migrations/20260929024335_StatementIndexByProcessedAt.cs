using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServicosFinanceiros.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementIndexByProcessedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_id_occurred_at",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_id_processed_at",
                table: "transactions",
                columns: new[] { "account_id", "processed_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_id_processed_at",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_id_occurred_at",
                table: "transactions",
                columns: new[] { "account_id", "occurred_at" },
                descending: new[] { false, true });
        }
    }
}
