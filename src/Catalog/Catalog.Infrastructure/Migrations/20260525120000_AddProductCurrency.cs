using Microsoft.EntityFrameworkCore.Migrations;

namespace Haworks.Catalog.Infrastructure.Migrations;

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
