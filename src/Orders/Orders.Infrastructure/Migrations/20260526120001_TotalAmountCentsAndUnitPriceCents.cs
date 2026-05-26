using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TotalAmountCentsAndUnitPriceCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orders.TotalAmount (numeric(18,2)) → TotalAmountCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE orders."Orders"
                    ADD COLUMN "TotalAmountCents" bigint NOT NULL DEFAULT 0;
                UPDATE orders."Orders"
                    SET "TotalAmountCents" = ROUND("TotalAmount" * 100)::bigint;
                ALTER TABLE orders."Orders"
                    DROP COLUMN "TotalAmount";
                ALTER TABLE orders."Orders"
                    ALTER COLUMN "TotalAmountCents" DROP DEFAULT;
                """);

            // OrderItems.UnitPrice (numeric(18,2)) → UnitPriceCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE orders."OrderItems"
                    ADD COLUMN "UnitPriceCents" bigint NOT NULL DEFAULT 0;
                UPDATE orders."OrderItems"
                    SET "UnitPriceCents" = ROUND("UnitPrice" * 100)::bigint;
                ALTER TABLE orders."OrderItems"
                    DROP COLUMN "UnitPrice";
                ALTER TABLE orders."OrderItems"
                    ALTER COLUMN "UnitPriceCents" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE orders."Orders"
                    ADD COLUMN "TotalAmount" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE orders."Orders"
                    SET "TotalAmount" = "TotalAmountCents" / 100.0;
                ALTER TABLE orders."Orders"
                    DROP COLUMN "TotalAmountCents";
                ALTER TABLE orders."Orders"
                    ALTER COLUMN "TotalAmount" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE orders."OrderItems"
                    ADD COLUMN "UnitPrice" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE orders."OrderItems"
                    SET "UnitPrice" = "UnitPriceCents" / 100.0;
                ALTER TABLE orders."OrderItems"
                    DROP COLUMN "UnitPriceCents";
                ALTER TABLE orders."OrderItems"
                    ALTER COLUMN "UnitPrice" DROP DEFAULT;
                """);
        }
    }
}
