using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Webhooks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_delivery_attempts",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "webhooks");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "SecretPreview",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "SecretPreview",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                schema: "webhooks",
                table: "webhook_subscriptions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "text", nullable: true),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    FinalStatus = table.Column<int>(type: "integer", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedFromIp = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_webhook_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "webhooks",
                        principalTable: "webhook_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_delivery_attempts",
                schema: "webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "text", nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedFromIp = table.Column<string>(type: "text", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_delivery_attempts_webhook_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalSchema: "webhooks",
                        principalTable: "webhook_deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_EventId",
                schema: "webhooks",
                table: "webhook_deliveries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_Status_NextAttemptAt",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" IN ('Pending', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_SubscriptionId_CreatedAt",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "SubscriptionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_SubscriptionId_EventId",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "SubscriptionId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_attempts_DeliveryId_AttemptIndex",
                schema: "webhooks",
                table: "webhook_delivery_attempts",
                columns: new[] { "DeliveryId", "AttemptIndex" });
        }
    }
}
