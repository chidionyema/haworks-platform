using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Haworks.Privacy.Infrastructure.Migrations;

/// <summary>
/// Moves privacy tables from public schema to privacy schema.
/// The original InitialCreate migration omitted the schema: parameter,
/// so tables landed in public. HasDefaultSchema("privacy") in the DbContext
/// makes all queries target the privacy schema, causing 42P01 errors.
/// </summary>
public partial class MoveToPrivacySchema : Migration
{
    private static readonly string[] Tables = [
        "PrivacyRequests", "PrivacyRequestSteps", "InboxState",
        "OutboxState", "OutboxMessage", "PrivacySagaStates", "SagaTransitionAudits"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "privacy");

        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = '{table}')
                       AND NOT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'privacy' AND tablename = '{table}') THEN
                        EXECUTE format('ALTER TABLE public.%I SET SCHEMA privacy', '{table}');
                    END IF;
                END $$;
            """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'privacy' AND tablename = '{table}') THEN
                        EXECUTE format('ALTER TABLE privacy.%I SET SCHEMA public', '{table}');
                    END IF;
                END $$;
            """);
        }
    }
}
