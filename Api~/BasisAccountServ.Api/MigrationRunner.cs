using Npgsql;

namespace BasisAccountServ.Api;

/// <summary>
/// Applies Migrations/*.sql in file-name order, once each, recording them in schema_migrations.
/// A PostgreSQL advisory lock serializes concurrent API replicas that start at the same time.
/// Databases created by the earlier "run 001 on every start" behaviour are safe: 001 is
/// idempotent (CREATE ... IF NOT EXISTS), so it is simply recorded on first run.
/// </summary>
public static class MigrationRunner
{
    private const long AdvisoryLockKey = 0x42415349534143L; // "BASISAC"

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, string migrationsDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(migrationsDirectory))
            throw new DirectoryNotFoundException("The migrations directory was not published: " + migrationsDirectory);

        var files = Directory.GetFiles(migrationsDirectory, "*.sql")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException("No database migrations were published.", migrationsDirectory);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using (var create = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )
                """, connection))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var file in files)
            {
                var version = Path.GetFileNameWithoutExtension(file);

                await using (var check = new NpgsqlCommand(
                    "SELECT 1 FROM schema_migrations WHERE version = @version", connection))
                {
                    check.Parameters.AddWithValue("version", version);
                    if (await check.ExecuteScalarAsync(cancellationToken) is not null)
                        continue;
                }

                var sql = await File.ReadAllTextAsync(file, cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using (var apply = new NpgsqlCommand(sql, connection, transaction))
                {
                    await apply.ExecuteNonQueryAsync(cancellationToken);
                }
                await using (var record = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (version) VALUES (@version)", connection, transaction))
                {
                    record.Parameters.AddWithValue("version", version);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
