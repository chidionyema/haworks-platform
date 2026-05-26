using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payouts.Infrastructure.Migrations
{
    public partial class AddSellerProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerProfiles",
                schema: "payouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalProviderId = table.Column<string>(type: "text", nullable: true),
                    KycStatus = table.Column<string>(type: "text", nullable: true),
                    PayoutsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PayoutSchedule = table.Column<string>(type: "text", nullable: false, defaultValue: "Monthly"),
                    PayoutThresholdCents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 5000L),
                    CommissionPercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 10.00m),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerProfiles_SellerId",
                schema: "payouts",
                table: "SellerProfiles",
                column: "SellerId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerProfiles",
                schema: "payouts");
        }
    }
}
