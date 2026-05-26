using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    public partial class AddRenewingTimeoutTokenId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RenewingTimeoutTokenId",
                table: "SubscriptionSagas",
                schema: "payments",
                type: "uuid",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenewingTimeoutTokenId",
                table: "SubscriptionSagas",
                schema: "payments");
        }
    }
}
