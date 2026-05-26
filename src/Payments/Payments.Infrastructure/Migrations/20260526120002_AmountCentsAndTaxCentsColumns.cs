using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AmountCentsAndTaxCentsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Payments: Amount (numeric(18,2)) → AmountCents (bigint)
            //           Tax (numeric) → TaxCents (bigint)
            //           TotalRefunded (numeric(18,2)) → TotalRefundedCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payments."Payments"
                    ADD COLUMN "AmountCents" bigint NOT NULL DEFAULT 0,
                    ADD COLUMN "TaxCents" bigint NOT NULL DEFAULT 0,
                    ADD COLUMN "TotalRefundedCents" bigint NOT NULL DEFAULT 0;
                UPDATE payments."Payments"
                    SET "AmountCents" = ROUND("Amount" * 100)::bigint,
                        "TaxCents" = ROUND("Tax" * 100)::bigint,
                        "TotalRefundedCents" = ROUND("TotalRefunded" * 100)::bigint;
                ALTER TABLE payments."Payments"
                    DROP COLUMN "Amount",
                    DROP COLUMN "Tax",
                    DROP COLUMN "TotalRefunded";
                ALTER TABLE payments."Payments"
                    ALTER COLUMN "AmountCents" DROP DEFAULT,
                    ALTER COLUMN "TaxCents" DROP DEFAULT,
                    ALTER COLUMN "TotalRefundedCents" DROP DEFAULT;
                """);

            // RefundSagas: Amount (numeric(18,2)) → AmountCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payments."RefundSagas"
                    ADD COLUMN "AmountCents" bigint NOT NULL DEFAULT 0;
                UPDATE payments."RefundSagas"
                    SET "AmountCents" = ROUND("Amount" * 100)::bigint;
                ALTER TABLE payments."RefundSagas"
                    DROP COLUMN "Amount";
                ALTER TABLE payments."RefundSagas"
                    ALTER COLUMN "AmountCents" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE payments."Payments"
                    ADD COLUMN "Amount" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN "Tax" numeric NOT NULL DEFAULT 0,
                    ADD COLUMN "TotalRefunded" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payments."Payments"
                    SET "Amount" = "AmountCents" / 100.0,
                        "Tax" = "TaxCents" / 100.0,
                        "TotalRefunded" = "TotalRefundedCents" / 100.0;
                ALTER TABLE payments."Payments"
                    DROP COLUMN "AmountCents",
                    DROP COLUMN "TaxCents",
                    DROP COLUMN "TotalRefundedCents";
                ALTER TABLE payments."Payments"
                    ALTER COLUMN "Amount" DROP DEFAULT,
                    ALTER COLUMN "Tax" DROP DEFAULT,
                    ALTER COLUMN "TotalRefunded" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE payments."RefundSagas"
                    ADD COLUMN "Amount" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payments."RefundSagas"
                    SET "Amount" = "AmountCents" / 100.0;
                ALTER TABLE payments."RefundSagas"
                    DROP COLUMN "AmountCents";
                ALTER TABLE payments."RefundSagas"
                    ALTER COLUMN "Amount" DROP DEFAULT;
                """);
        }
    }
}
