using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionSagas_ProviderSubscriptionId",
                schema: "payments",
                table: "SubscriptionSagas");

            migrationBuilder.AddColumn<Guid>(
                name: "RenewingTimeoutTokenId",
                schema: "payments",
                table: "SubscriptionSagas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "TotalRefundedCents",
                schema: "payments",
                table: "Payments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSagas_ProviderSubscriptionId",
                schema: "payments",
                table: "SubscriptionSagas",
                column: "ProviderSubscriptionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionSagas_ProviderSubscriptionId",
                schema: "payments",
                table: "SubscriptionSagas");

            migrationBuilder.DropColumn(
                name: "RenewingTimeoutTokenId",
                schema: "payments",
                table: "SubscriptionSagas");

            migrationBuilder.AlterColumn<long>(
                name: "TotalRefundedCents",
                schema: "payments",
                table: "Payments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSagas_ProviderSubscriptionId",
                schema: "payments",
                table: "SubscriptionSagas",
                column: "ProviderSubscriptionId");
        }
    }
}
