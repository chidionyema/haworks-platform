#pragma warning disable CA1861
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Privacy.Infrastructure.Migrations
{
    public partial class AddUniqueConstraintForActivePrivacyRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add unique constraint to prevent multiple pending/in-progress privacy requests for same user
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IX_PrivacyRequests_UserId_ActiveRequest
                ON privacy.""PrivacyRequests"" (""UserId"")
                WHERE ""Status"" IN (0, 1)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX privacy.IX_PrivacyRequests_UserId_ActiveRequest");
        }
    }
}