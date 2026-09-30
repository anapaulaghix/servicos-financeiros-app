using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServicosFinanceiros.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementOrderBySequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_id_processed_at",
                table: "transactions");

            // Um AddColumn com identity numeraria as linhas existentes na ordem física da tabela, que é
            // arbitrária. Aqui o histórico já gravado é numerado na ordem em que o extrato o mostrava
            // (processed_at, event_id) e só então a coluna vira identity, continuando do maior valor.
            migrationBuilder.Sql("ALTER TABLE transactions ADD COLUMN sequence bigint;");
            migrationBuilder.Sql(
                """
                UPDATE transactions AS t
                SET sequence = ordered.position
                FROM (
                    SELECT event_id, row_number() OVER (ORDER BY processed_at, event_id) AS position
                    FROM transactions
                ) AS ordered
                WHERE t.event_id = ordered.event_id;
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE transactions ALTER COLUMN sequence SET NOT NULL;
                ALTER TABLE transactions ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY;
                SELECT setval(
                    pg_get_serial_sequence('transactions', 'sequence'),
                    (SELECT COALESCE(MAX(sequence), 0) + 1 FROM transactions),
                    false);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_id_sequence",
                table: "transactions",
                columns: new[] { "account_id", "sequence" },
                unique: true,
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_id_sequence",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_id_processed_at",
                table: "transactions",
                columns: new[] { "account_id", "processed_at" },
                descending: new[] { false, true });
        }
    }
}
