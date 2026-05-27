using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockReservations_OrderId",
                schema: "catalog",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                schema: "catalog",
                table: "StockReservations",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockReservations_OrderId",
                schema: "catalog",
                table: "StockReservations");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "StockReservations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "Products",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                schema: "catalog",
                table: "StockReservations",
                column: "OrderId");
        }
    }
}
