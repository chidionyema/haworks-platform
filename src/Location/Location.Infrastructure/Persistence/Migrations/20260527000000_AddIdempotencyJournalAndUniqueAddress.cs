#pragma warning disable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Location.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyJournalAndUniqueAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotency journal table for at-most-once command execution
            migrationBuilder.CreateTable(
                name: "IdempotencyJournal",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CommandType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyJournal", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyJournal_IdempotencyKey",
                schema: "location",
                table: "IdempotencyJournal",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyJournal_ExpiresAt",
                schema: "location",
                table: "IdempotencyJournal",
                column: "ExpiresAt");

            // Unique constraint on address fields to prevent duplicates
            migrationBuilder.CreateIndex(
                name: "IX_Addresses_Unique_Address",
                schema: "location",
                table: "Addresses",
                columns: new[] { "Street", "City", "Postcode", "Country" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Addresses_Unique_Address",
                schema: "location",
                table: "Addresses");

            migrationBuilder.DropTable(
                name: "IdempotencyJournal",
                schema: "location");
        }
    }
}
