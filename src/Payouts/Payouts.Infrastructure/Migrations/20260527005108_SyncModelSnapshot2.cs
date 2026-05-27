using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payouts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_ReferenceId",
                schema: "payouts",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "payouts",
                table: "LedgerAccounts");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "payouts",
                table: "Payouts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitReference",
                schema: "payouts",
                table: "Payouts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReferenceId_AccountId",
                schema: "payouts",
                table: "LedgerEntries",
                columns: new[] { "ReferenceId", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_ReferenceId_AccountId",
                schema: "payouts",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "payouts",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "TransitReference",
                schema: "payouts",
                table: "Payouts");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "payouts",
                table: "LedgerAccounts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReferenceId",
                schema: "payouts",
                table: "LedgerEntries",
                column: "ReferenceId");
        }
    }
}
