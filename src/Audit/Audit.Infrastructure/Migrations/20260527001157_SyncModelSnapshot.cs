using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.RenameTable(
                name: "audit_export_jobs",
                newName: "audit_export_jobs",
                newSchema: "audit");

            migrationBuilder.RenameTable(
                name: "audit_events",
                newName: "audit_events",
                newSchema: "audit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "audit_export_jobs",
                schema: "audit",
                newName: "audit_export_jobs");

            migrationBuilder.RenameTable(
                name: "audit_events",
                schema: "audit",
                newName: "audit_events");
        }
    }
}
