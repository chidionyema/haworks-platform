using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payouts.Infrastructure.Migrations
{
    public partial class AddMissingPayoutColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payouts",
                schema: "payouts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitReference",
                table: "Payouts",
                schema: "payouts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IdempotencyKey", table: "Payouts", schema: "payouts");
            migrationBuilder.DropColumn(name: "TransitReference", table: "Payouts", schema: "payouts");
        }
    }
}
