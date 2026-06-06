#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Scheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaultLeases",
                schema: "scheduler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CredentialType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LeaseId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultLeases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RotationAuditEntries",
                schema: "scheduler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NewLeaseId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultLeases_ServiceName_RoleName_CredentialType",
                schema: "scheduler",
                table: "VaultLeases",
                columns: new[] { "ServiceName", "RoleName", "CredentialType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaultLeases_Status_ExpiresAt",
                schema: "scheduler",
                table: "VaultLeases",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RotationAuditEntries_LeaseId",
                schema: "scheduler",
                table: "RotationAuditEntries",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RotationAuditEntries_Timestamp",
                schema: "scheduler",
                table: "RotationAuditEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VaultLeases",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "RotationAuditEntries",
                schema: "scheduler");
        }
    }
}