using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Payments.Infrastructure.Migrations
{
    public partial class AddRenewingTimeoutTokenId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Superseded by 20260528000000_FixSubscriptionSagaSchema
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: reversed by FixSubscriptionSagaSchema
        }
    }
}
