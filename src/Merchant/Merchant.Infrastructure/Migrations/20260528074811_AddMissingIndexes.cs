using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Merchants_Status",
                schema: "merchant",
                table: "Merchants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_CreatedAt",
                schema: "merchant",
                table: "Merchants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_Status_CreatedAt",
                schema: "merchant",
                table: "Merchants",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_Status_CreatedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_CreatedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_Status",
                schema: "merchant",
                table: "Merchants");
        }
    }
}
