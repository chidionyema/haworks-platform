using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Privacy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiatedBy",
                schema: "privacy",
                table: "SagaTransitionAudit",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitiatedBy",
                schema: "privacy",
                table: "SagaTransitionAudit");
        }
    }
}
