using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Media.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.AddColumn<string>(
                name: "BucketName",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "media",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ETag",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "EntityId",
                schema: "media",
                table: "MediaFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "media",
                table: "MediaFiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ObjectName",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "media",
                table: "MediaFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "media",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                schema: "media",
                table: "MediaFiles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAt",
                schema: "media",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaMetadata",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaVersions",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ObjectName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaMetadata_MediaFileId_Key",
                schema: "media",
                table: "MediaMetadata",
                columns: new[] { "MediaFileId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaVersions_MediaFileId_VersionNumber",
                schema: "media",
                table: "MediaVersions",
                columns: new[] { "MediaFileId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaMetadata",
                schema: "media");

            migrationBuilder.DropTable(
                name: "MediaVersions",
                schema: "media");

            migrationBuilder.DropColumn(
                name: "BucketName",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "ETag",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "EntityId",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "EntityType",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "ObjectName",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "QuarantineReason",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                schema: "media",
                table: "MediaFiles");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "media",
                table: "MediaFiles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }
    }
}
