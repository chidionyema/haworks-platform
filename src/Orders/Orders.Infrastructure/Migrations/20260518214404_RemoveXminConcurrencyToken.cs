using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a Postgres system column — it cannot be added or dropped
            // via DDL. Its removal from the EF model is a metadata-only change;
            // no DDL statement is required or valid here.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: xmin is a system column and was never created by EF.
        }
    }
}
