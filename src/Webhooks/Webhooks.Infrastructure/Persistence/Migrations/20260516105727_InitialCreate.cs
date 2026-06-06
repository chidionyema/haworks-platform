#pragma warning disable CA1861
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Webhooks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "webhooks");

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    SecretHash = table.Column<string>(type: "text", nullable: false),
                    SecretPreview = table.Column<string>(type: "text", nullable: false),
                    Events = table.Column<string[]>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "text", nullable: true),
                    ModifiedFromIp = table.Column<string>(type: "text", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscriptions", x => x.Id);
                });



            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscriptions_Events",
                schema: "webhooks",
                table: "webhook_subscriptions",
                column: "Events")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscriptions_PartnerId",
                schema: "webhooks",
                table: "webhook_subscriptions",
                column: "PartnerId",
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "webhooks");
        }
    }
}
