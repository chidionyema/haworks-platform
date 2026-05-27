using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Pricing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "TieredPrices");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "PromotionCodes");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "PriceRules");

            migrationBuilder.CreateIndex(
                name: "IX_CalculationLogs_UserId",
                schema: "pricing",
                table: "CalculationLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalculationLogs_UserId",
                schema: "pricing",
                table: "CalculationLogs");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "TieredPrices",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "TaxRates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "PromotionCodes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "PriceRules",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }
    }
}
