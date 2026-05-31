using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderMessageIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ProviderMessageId",
                schema: "notifications",
                table: "Notifications",
                column: "ProviderMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_ProviderMessageId",
                schema: "notifications",
                table: "Notifications");
        }
    }
}