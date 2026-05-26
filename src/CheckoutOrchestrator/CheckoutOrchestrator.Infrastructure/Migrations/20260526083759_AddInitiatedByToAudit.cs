using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.CheckoutOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInitiatedByToAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiatedBy",
                schema: "checkout",
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
                schema: "checkout",
                table: "SagaTransitionAudit");
        }
    }
}
