using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Pricing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyToPriceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "pricing",
                table: "PriceRules",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "pricing",
                table: "PromotionCodes",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "pricing",
                table: "PriceRules");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "pricing",
                table: "PromotionCodes");
        }
    }
}
