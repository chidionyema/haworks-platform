using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payouts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BalanceAndAmountCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LedgerAccounts: Balance (numeric(18,2)) → BalanceCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."LedgerAccounts"
                    ADD COLUMN "BalanceCents" bigint NOT NULL DEFAULT 0;
                UPDATE payouts."LedgerAccounts"
                    SET "BalanceCents" = ROUND("Balance" * 100)::bigint;
                ALTER TABLE payouts."LedgerAccounts"
                    DROP COLUMN "Balance";
                ALTER TABLE payouts."LedgerAccounts"
                    ALTER COLUMN "BalanceCents" DROP DEFAULT;
                """);

            // LedgerEntries: Amount (numeric(18,2)) → AmountCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."LedgerEntries"
                    ADD COLUMN "AmountCents" bigint NOT NULL DEFAULT 0;
                UPDATE payouts."LedgerEntries"
                    SET "AmountCents" = ROUND("Amount" * 100)::bigint;
                ALTER TABLE payouts."LedgerEntries"
                    DROP COLUMN "Amount";
                ALTER TABLE payouts."LedgerEntries"
                    ALTER COLUMN "AmountCents" DROP DEFAULT;
                """);

            // Payouts: Amount (numeric(18,2)) → AmountCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."Payouts"
                    ADD COLUMN "AmountCents" bigint NOT NULL DEFAULT 0;
                UPDATE payouts."Payouts"
                    SET "AmountCents" = ROUND("Amount" * 100)::bigint;
                ALTER TABLE payouts."Payouts"
                    DROP COLUMN "Amount";
                ALTER TABLE payouts."Payouts"
                    ALTER COLUMN "AmountCents" DROP DEFAULT;
                """);

            // SellerProfiles: PayoutThreshold (numeric(18,2)) → PayoutThresholdCents (bigint)
            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."SellerProfiles"
                    ADD COLUMN "PayoutThresholdCents" bigint NOT NULL DEFAULT 5000;
                UPDATE payouts."SellerProfiles"
                    SET "PayoutThresholdCents" = ROUND("PayoutThreshold" * 100)::bigint;
                ALTER TABLE payouts."SellerProfiles"
                    DROP COLUMN "PayoutThreshold";
                ALTER TABLE payouts."SellerProfiles"
                    ALTER COLUMN "PayoutThresholdCents" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."LedgerAccounts"
                    ADD COLUMN "Balance" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payouts."LedgerAccounts"
                    SET "Balance" = "BalanceCents" / 100.0;
                ALTER TABLE payouts."LedgerAccounts"
                    DROP COLUMN "BalanceCents";
                ALTER TABLE payouts."LedgerAccounts"
                    ALTER COLUMN "Balance" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."LedgerEntries"
                    ADD COLUMN "Amount" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payouts."LedgerEntries"
                    SET "Amount" = "AmountCents" / 100.0;
                ALTER TABLE payouts."LedgerEntries"
                    DROP COLUMN "AmountCents";
                ALTER TABLE payouts."LedgerEntries"
                    ALTER COLUMN "Amount" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."Payouts"
                    ADD COLUMN "Amount" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payouts."Payouts"
                    SET "Amount" = "AmountCents" / 100.0;
                ALTER TABLE payouts."Payouts"
                    DROP COLUMN "AmountCents";
                ALTER TABLE payouts."Payouts"
                    ALTER COLUMN "Amount" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE payouts."SellerProfiles"
                    ADD COLUMN "PayoutThreshold" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE payouts."SellerProfiles"
                    SET "PayoutThreshold" = "PayoutThresholdCents" / 100.0;
                ALTER TABLE payouts."SellerProfiles"
                    DROP COLUMN "PayoutThresholdCents";
                ALTER TABLE payouts."SellerProfiles"
                    ALTER COLUMN "PayoutThreshold" DROP DEFAULT;
                """);
        }
    }
}
