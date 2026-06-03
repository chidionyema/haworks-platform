using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixOwnerIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants",
                column: "OwnerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants",
                column: "OwnerId");
        }
    }
}