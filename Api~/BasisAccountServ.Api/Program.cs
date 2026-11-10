using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AccountServ")
    ?? Environment.GetEnvironmentVariable("ACCOUNTSERV_DATABASE_URL");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings__AccountServ or ACCOUNTSERV_DATABASE_URL.");

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "basis-accountserv-api" }));
app.MapGet("/health/ready", async (NpgsqlDataSource database, CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return Results.Ok(new { status = "ready", service = "basis-accountserv-api", database = "connected" });
    }
    catch (NpgsqlException)
    {
        return Results.Json(new { status = "not_ready", service = "basis-accountserv-api", database = "unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

var migrationPath = Path.Combine(AppContext.BaseDirectory, "Migrations", "001_initial_schema.sql");
if (!File.Exists(migrationPath))
    throw new FileNotFoundException("The initial database migration was not published.", migrationPath);

var migrationSql = await File.ReadAllTextAsync(migrationPath);
await using (var connection = await dataSource.OpenConnectionAsync())
await using (var transaction = await connection.BeginTransactionAsync())
{
    await using var command = new NpgsqlCommand(migrationSql, connection, transaction);
    await command.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
}

app.Lifetime.ApplicationStopped.Register(dataSource.Dispose);
app.Run();

public partial class Program;
