using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.CheckoutOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeyUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagas_IdempotencyKey",
                schema: "checkout",
                table: "CheckoutSagas",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagas_CurrentState_CreatedAt",
                schema: "checkout",
                table: "CheckoutSagas",
                columns: new[] { "CurrentState", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckoutSagas_CurrentState_CreatedAt",
                schema: "checkout",
                table: "CheckoutSagas");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutSagas_IdempotencyKey",
                schema: "checkout",
                table: "CheckoutSagas");
        }
    }
}
