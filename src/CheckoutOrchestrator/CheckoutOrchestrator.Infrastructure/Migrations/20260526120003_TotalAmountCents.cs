using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.CheckoutOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TotalAmountCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CheckoutSagas: TotalAmount (numeric(18,2)) → TotalAmountCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE checkout."CheckoutSagas"
                    ADD COLUMN "TotalAmountCents" bigint NOT NULL DEFAULT 0;
                UPDATE checkout."CheckoutSagas"
                    SET "TotalAmountCents" = CASE
                        WHEN "TotalAmount" >= 0
                        THEN FLOOR("TotalAmount" * 100 + 0.5)::bigint
                        ELSE CEIL("TotalAmount" * 100 - 0.5)::bigint
                    END;
                ALTER TABLE checkout."CheckoutSagas"
                    DROP COLUMN "TotalAmount";
                ALTER TABLE checkout."CheckoutSagas"
                    ALTER COLUMN "TotalAmountCents" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE checkout."CheckoutSagas"
                    ADD COLUMN "TotalAmount" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE checkout."CheckoutSagas"
                    SET "TotalAmount" = "TotalAmountCents" / 100.0;
                ALTER TABLE checkout."CheckoutSagas"
                    DROP COLUMN "TotalAmountCents";
                ALTER TABLE checkout."CheckoutSagas"
                    ALTER COLUMN "TotalAmount" DROP DEFAULT;
                """);
        }
    }
}
