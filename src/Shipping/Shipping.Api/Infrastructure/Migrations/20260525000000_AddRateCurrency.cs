using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Shipping.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddRateCurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RateCurrency",
            table: "Shipments",
            schema: "shipping",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "USD");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RateCurrency",
            table: "Shipments",
            schema: "shipping");
    }
}
