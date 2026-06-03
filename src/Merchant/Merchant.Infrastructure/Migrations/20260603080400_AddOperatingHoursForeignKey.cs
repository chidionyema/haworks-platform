using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatingHoursForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_OperatingHours_Merchants_MerchantId",
                schema: "merchant",
                table: "OperatingHours",
                column: "MerchantId",
                principalSchema: "merchant",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperatingHours_Merchants_MerchantId",
                schema: "merchant",
                table: "OperatingHours");
        }
    }
}