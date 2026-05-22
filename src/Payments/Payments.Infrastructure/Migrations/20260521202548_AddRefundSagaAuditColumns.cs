using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundSagaAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiatedBy",
                schema: "payments",
                table: "SagaTransitionAudit",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                schema: "payments",
                table: "RefundSagas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewEscalationTokenId",
                schema: "payments",
                table: "RefundSagas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "payments",
                table: "RefundSagas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitiatedBy",
                schema: "payments",
                table: "SagaTransitionAudit");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                schema: "payments",
                table: "RefundSagas");

            migrationBuilder.DropColumn(
                name: "ReviewEscalationTokenId",
                schema: "payments",
                table: "RefundSagas");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "payments",
                table: "RefundSagas");

        }
    }
}
