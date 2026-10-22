using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>
/// Applies the Payments API's own migrations (Migrations/P*.sql) in name order. Runs with
/// the schema owner's connection; the runtime role only receives the grants the scripts give it.
/// Already-applied scripts are recorded in payments.schemaversions and never run twice.
/// </summary>
public static partial class PaymentsMigrator
{
    public const string Schema = "payments";

    public static MigrationResult Run(string ownerConnectionString, string runtimeRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerConnectionString);
        if (!RoleName().IsMatch(runtimeRole))
        {
            // The role name is substituted into GRANT statements, so it must be a plain identifier.
            throw new ArgumentException($"'{runtimeRole}' is not a valid role name.", nameof(runtimeRole));
        }

        // DbUp creates its journal table before running the first script, so the
        // schema that holds it must exist first.
        using (var conn = new NpgsqlConnection(ownerConnectionString))
        {
            conn.Execute($"CREATE SCHEMA IF NOT EXISTS {Schema}");
        }

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(ownerConnectionString)
            .WithScriptsEmbeddedInAssembly(typeof(PaymentsMigrator).Assembly, name => name.Contains(".Migrations.P", StringComparison.Ordinal))
            .JournalToPostgresqlTable(Schema, "schemaversions")
            .WithVariable("runtime_role", runtimeRole)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new MigrationFailedException(result.ErrorScript?.Name ?? "unknown script", result.Error);
        }
        return new MigrationResult(result.Scripts.Select(s => s.Name).ToArray());
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex RoleName();
}

/// <summary>The scripts applied by one run, in order. Empty when the schema was already current.</summary>
public sealed record MigrationResult(IReadOnlyList<string> AppliedScripts);

public sealed class MigrationFailedException(string script, Exception inner)
    : Exception($"Migration {script} failed: {inner.Message}", inner);
