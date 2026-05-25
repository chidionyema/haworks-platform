#pragma warning disable CA1861
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payouts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionRateBps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommissionRateBps",
                schema: "payouts",
                table: "LedgerEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionRateBps",
                schema: "payouts",
                table: "LedgerEntries");
        }
    }
}
