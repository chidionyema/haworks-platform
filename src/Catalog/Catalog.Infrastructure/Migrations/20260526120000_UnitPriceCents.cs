using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnitPriceCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename UnitPrice → UnitPriceCents and convert decimal → bigint (×100)
            migrationBuilder.Sql(
                """
                ALTER TABLE catalog."Products"
                    ADD COLUMN "UnitPriceCents" bigint NOT NULL DEFAULT 0;
                UPDATE catalog."Products"
                    SET "UnitPriceCents" = ROUND("UnitPrice" * 100)::bigint;
                ALTER TABLE catalog."Products"
                    DROP COLUMN "UnitPrice";
                ALTER TABLE catalog."Products"
                    ALTER COLUMN "UnitPriceCents" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE catalog."Products"
                    ADD COLUMN "UnitPrice" numeric(18,2) NOT NULL DEFAULT 0;
                UPDATE catalog."Products"
                    SET "UnitPrice" = "UnitPriceCents" / 100.0;
                ALTER TABLE catalog."Products"
                    DROP COLUMN "UnitPriceCents";
                ALTER TABLE catalog."Products"
                    ALTER COLUMN "UnitPrice" DROP DEFAULT;
                """);
        }
    }
}
