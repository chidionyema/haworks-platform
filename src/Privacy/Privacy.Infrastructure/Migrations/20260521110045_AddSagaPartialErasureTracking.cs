using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Privacy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaPartialErasureTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // C2: track which services failed (partial erasure recovery)
            migrationBuilder.AddColumn<string>(
                name: "FailedServices",
                schema: "privacy",
                table: "PrivacyRequestState",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // H9: per-service completion timestamps for GDPR audit trail
            migrationBuilder.AddColumn<DateTime>(
                name: "IdentityCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrdersCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentsCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedServices",
                schema: "privacy",
                table: "PrivacyRequestState");

            migrationBuilder.DropColumn(
                name: "IdentityCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState");

            migrationBuilder.DropColumn(
                name: "OrdersCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState");

            migrationBuilder.DropColumn(
                name: "PaymentsCompletedAt",
                schema: "privacy",
                table: "PrivacyRequestState");
        }
    }
}
