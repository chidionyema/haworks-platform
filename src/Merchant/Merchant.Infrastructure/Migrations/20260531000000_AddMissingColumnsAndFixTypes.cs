using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumnsAndFixTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, add all missing columns to Merchants table
            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            // Fix Status column type - change from integer to varchar(20)
            // First update existing values to string equivalents
            migrationBuilder.Sql(@"
                UPDATE merchant.""Merchants""
                SET ""Status"" = CASE
                    WHEN ""Status"" = 0 THEN 'Pending'
                    WHEN ""Status"" = 1 THEN 'Active'
                    WHEN ""Status"" = 2 THEN 'Suspended'
                    WHEN ""Status"" = 3 THEN 'Rejected'
                    WHEN ""Status"" = 4 THEN 'Deactivated'
                    WHEN ""Status"" = 5 THEN 'Maintenance'
                    ELSE 'Pending'
                END::varchar;
            ");

            // Change column type
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            // Add IsOpen column to OperatingHours table
            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                schema: "merchant",
                table: "OperatingHours",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove added columns from Merchants table
            migrationBuilder.DropColumn(name: "LogoUrl", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "Description", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "ContactEmail", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "ContactPhone", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "Category", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "Website", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "Timezone", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "RejectionReason", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "SuspensionReason", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "ApprovedBy", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "RejectedBy", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "SuspendedBy", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "DeactivatedBy", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "ApprovedAt", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "RejectedAt", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "SuspendedAt", schema: "merchant", table: "Merchants");
            migrationBuilder.DropColumn(name: "DeactivatedAt", schema: "merchant", table: "Merchants");

            // Revert Status column back to integer
            migrationBuilder.AlterColumn<int>(
                name: "Status",
                schema: "merchant",
                table: "Merchants",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            // Remove IsOpen column from OperatingHours table
            migrationBuilder.DropColumn(name: "IsOpen", schema: "merchant", table: "OperatingHours");
        }
    }
}