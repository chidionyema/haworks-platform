using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaTransitionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SagaTransitionAudit",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    SagaType = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<string>(type: "text", nullable: false),
                    ToState = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaTransitionAudit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SagaTransitionAudit_SagaType_CorrelationId",
                schema: "payments",
                table: "SagaTransitionAudit",
                columns: new[] { "SagaType", "CorrelationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SagaTransitionAudit",
                schema: "payments");
        }
    }
}
