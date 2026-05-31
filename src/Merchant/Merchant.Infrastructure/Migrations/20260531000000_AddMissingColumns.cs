#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                schema: "merchant",
                table: "OperatingHours",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Add foreign key constraint for OperatingHours -> Merchants
            migrationBuilder.AddForeignKey(
                name: "FK_OperatingHours_Merchants_MerchantId",
                schema: "merchant",
                table: "OperatingHours",
                column: "MerchantId",
                principalSchema: "merchant",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Add check constraint for operating hours time validation
            migrationBuilder.Sql(@"
                ALTER TABLE merchant.""OperatingHours""
                ADD CONSTRAINT ""CK_OperatingHours_ValidTimes""
                CHECK (""OpenTime"" < ""CloseTime"" OR (""OpenTime"" = '00:00:00' AND ""CloseTime"" = '00:00:00'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperatingHours_Merchants_MerchantId",
                schema: "merchant",
                table: "OperatingHours");

            migrationBuilder.Sql(@"ALTER TABLE merchant.""OperatingHours"" DROP CONSTRAINT IF EXISTS ""CK_OperatingHours_ValidTimes""");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Website",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Timezone",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspendedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "IsOpen",
                schema: "merchant",
                table: "OperatingHours");
        }
    }
}