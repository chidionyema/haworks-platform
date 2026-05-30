using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPlanPriceCentsAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-preserving order: add the new columns, BACKFILL from the old decimal
            // Price, THEN drop Price. Legacy rows predate the Currency column so they are
            // implicitly USD (2-decimal) — *100 is correct for them specifically. New rows
            // are always written via the app with an explicit currency + minor units.
            migrationBuilder.AddColumn<long>(
                name: "PriceCents",
                schema: "payments",
                table: "SubscriptionPlans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "payments",
                table: "SubscriptionPlans",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.Sql(
                "UPDATE \"payments\".\"SubscriptionPlans\" " +
                "SET \"PriceCents\" = CAST(ROUND(\"Price\" * 100) AS bigint), \"Currency\" = 'USD';");

            migrationBuilder.DropColumn(
                name: "Price",
                schema: "payments",
                table: "SubscriptionPlans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric data-preserving rollback: re-add Price, backfill from PriceCents
            // (÷100, 2-decimal — the original column was numeric(18,2)), then drop the new columns.
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "payments",
                table: "SubscriptionPlans",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                "UPDATE \"payments\".\"SubscriptionPlans\" " +
                "SET \"Price\" = \"PriceCents\" / 100.0;");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "payments",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PriceCents",
                schema: "payments",
                table: "SubscriptionPlans");
        }
    }
}
