using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "catalog",
                table: "Products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                // 'USD' (not "") so the column default is a valid ISO 4217 code; the app always
                // sets Currency explicitly, this only guards rows added without it.
                defaultValue: "USD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "catalog",
                table: "Products");
        }
    }
}
