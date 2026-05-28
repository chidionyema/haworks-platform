using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    public partial class FixSubscriptionSagaSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add missing column
            migrationBuilder.AddColumn<Guid>(
                name: "RenewingTimeoutTokenId",
                table: "SubscriptionSagas",
                schema: "payments",
                type: "uuid",
                nullable: true);

            // Remove columns no longer in model
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SubscriptionSagas",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "SubscriptionSagas",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "SubscriptionSagas",
                schema: "payments");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenewingTimeoutTokenId",
                table: "SubscriptionSagas",
                schema: "payments");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SubscriptionSagas",
                schema: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "SubscriptionSagas",
                schema: "payments",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAt",
                table: "SubscriptionSagas",
                schema: "payments",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
