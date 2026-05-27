using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Merchant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_OwnerId",
                table: "Merchants");

            migrationBuilder.EnsureSchema(
                name: "merchant");

            migrationBuilder.RenameTable(
                name: "OutboxState",
                newName: "OutboxState",
                newSchema: "merchant");

            migrationBuilder.RenameTable(
                name: "OutboxMessage",
                newName: "OutboxMessage",
                newSchema: "merchant");

            migrationBuilder.RenameTable(
                name: "OperatingHours",
                newName: "OperatingHours",
                newSchema: "merchant");

            migrationBuilder.RenameTable(
                name: "Merchants",
                newName: "Merchants",
                newSchema: "merchant");

            migrationBuilder.RenameTable(
                name: "InboxState",
                newName: "InboxState",
                newSchema: "merchant");

            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                schema: "merchant",
                table: "OperatingHours",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
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
                name: "Category",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
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

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                schema: "merchant",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedBy",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                schema: "merchant",
                table: "Merchants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants",
                column: "OwnerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_OwnerId",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "IsOpen",
                schema: "merchant",
                table: "OperatingHours");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Category",
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
                name: "DeactivatedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspendedBy",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Timezone",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "Website",
                schema: "merchant",
                table: "Merchants");

            migrationBuilder.RenameTable(
                name: "OutboxState",
                schema: "merchant",
                newName: "OutboxState");

            migrationBuilder.RenameTable(
                name: "OutboxMessage",
                schema: "merchant",
                newName: "OutboxMessage");

            migrationBuilder.RenameTable(
                name: "OperatingHours",
                schema: "merchant",
                newName: "OperatingHours");

            migrationBuilder.RenameTable(
                name: "Merchants",
                schema: "merchant",
                newName: "Merchants");

            migrationBuilder.RenameTable(
                name: "InboxState",
                schema: "merchant",
                newName: "InboxState");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Merchants",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Merchants",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Merchants",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "Merchants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_OwnerId",
                table: "Merchants",
                column: "OwnerId");
        }
    }
}
