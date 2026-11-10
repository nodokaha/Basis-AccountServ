using Npgsql;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using BasisAccountServ.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AccountServ")
    ?? Environment.GetEnvironmentVariable("ACCOUNTSERV_DATABASE_URL");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings__AccountServ or ACCOUNTSERV_DATABASE_URL.");

var didIssuer = builder.Configuration["DID_PROOF_ISSUER"]?.TrimEnd('/');
if (!string.IsNullOrWhiteSpace(didIssuer) &&
    (!Uri.TryCreate(didIssuer, UriKind.Absolute, out var issuerUri) ||
     issuerUri.Scheme != Uri.UriSchemeHttps ||
     !string.IsNullOrEmpty(issuerUri.Query) ||
     !string.IsNullOrEmpty(issuerUri.Fragment)))
{
    throw new InvalidOperationException("DID_PROOF_ISSUER must be a canonical HTTPS URL without query or fragment.");
}
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


app.MapPost("/api/did/link/challenges", async (
    CreateDidLinkChallengeRequest request,
    HttpContext context,
    NpgsqlDataSource database,
    CancellationToken cancellationToken) =>
{
    if (!TryGetAccountId(context.User, out var accountId))
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(didIssuer))
        return Results.Problem("DID_PROOF_ISSUER is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (!BasisDidKey.TryDecodeEd25519PublicKey(request.Did, out _))
        return Results.BadRequest(new { error = "invalid_did_key" });

    await using var connection = await database.OpenConnectionAsync(cancellationToken);
    await using (var accountCommand = new NpgsqlCommand(
        "SELECT 1 FROM accounts WHERE account_id = @account_id AND disabled_at IS NULL",
        connection))
    {
        accountCommand.Parameters.AddWithValue("account_id", accountId);
        if (await accountCommand.ExecuteScalarAsync(cancellationToken) is null)
            return Results.Unauthorized();
    }

    await using (var existingCommand = new NpgsqlCommand(
        "SELECT account_id FROM account_did_keys WHERE did = @did",
        connection))
    {
        existingCommand.Parameters.AddWithValue("did", request.Did);
        var existing = await existingCommand.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
            return Results.Conflict(new { error = "did_already_linked" });
    }

    var challengeId = Guid.NewGuid();
    var nonce = RandomNumberGenerator.GetBytes(32);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
    var payload = DidLinkPayload.Create(didIssuer, accountId, request.Did, challengeId, expiresAt, nonce);
    var nonceHash = SHA256.HashData(nonce);

    await using var insert = new NpgsqlCommand("""
        INSERT INTO basis_did_link_challenges
            (challenge_id, account_id, did, nonce_hash, verifier_context, signed_payload, expires_at)
        VALUES
            (@challenge_id, @account_id, @did, @nonce_hash, @verifier_context, @signed_payload, @expires_at)
        """, connection);
    insert.Parameters.AddWithValue("challenge_id", challengeId);
    insert.Parameters.AddWithValue("account_id", accountId);
    insert.Parameters.AddWithValue("did", request.Did);
    insert.Parameters.AddWithValue("nonce_hash", nonceHash);
    insert.Parameters.AddWithValue("verifier_context", DidLinkPayload.Purpose);
    insert.Parameters.AddWithValue("signed_payload", payload);
    insert.Parameters.AddWithValue("expires_at", expiresAt);
    await insert.ExecuteNonQueryAsync(cancellationToken);

    return Results.Ok(new
    {
        challengeId,
        did = request.Did,
        payload = WebEncoders.Base64UrlEncode(payload),
        expiresAt
    });
});

app.MapPost("/api/did/link/challenges/{challengeId:guid}/complete", async (
    Guid challengeId,
    CompleteDidLinkChallengeRequest request,
    HttpContext context,
    NpgsqlDataSource database,
    CancellationToken cancellationToken) =>
{
    if (!TryGetAccountId(context.User, out var accountId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Signature) || request.Signature.Length > 128)
        return Results.BadRequest(new { error = "invalid_signature_encoding" });

    byte[] signature;
    try { signature = WebEncoders.Base64UrlDecode(request.Signature); }
    catch (FormatException) { return Results.BadRequest(new { error = "invalid_signature_encoding" }); }
    if (signature.Length != 64)
        return Results.BadRequest(new { error = "invalid_signature_length" });

    await using var connection = await database.OpenConnectionAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    string did;
    byte[] payload;
    DateTime expiresAt;
    await using (var select = new NpgsqlCommand("""
        SELECT c.did, c.signed_payload, c.expires_at, c.consumed_at
        FROM basis_did_link_challenges c
        INNER JOIN accounts a ON a.account_id = c.account_id AND a.disabled_at IS NULL
        WHERE c.challenge_id = @challenge_id AND c.account_id = @account_id
        FOR UPDATE OF c
        """, connection, transaction))
    {
        select.Parameters.AddWithValue("challenge_id", challengeId);
        select.Parameters.AddWithValue("account_id", accountId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Results.NotFound(new { error = "challenge_not_found" });
        did = reader.GetString(0);
        payload = reader.GetFieldValue<byte[]>(1);
        expiresAt = reader.GetDateTime(2);
        var consumedAt = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
        if (consumedAt is not null || expiresAt <= DateTime.UtcNow)
            return Results.Conflict(new { error = "challenge_expired_or_consumed" });
    }

    await using (var consume = new NpgsqlCommand("""
        UPDATE basis_did_link_challenges SET consumed_at = NOW()
        WHERE challenge_id = @challenge_id AND account_id = @account_id AND consumed_at IS NULL
        """, connection, transaction))
    {
        consume.Parameters.AddWithValue("challenge_id", challengeId);
        consume.Parameters.AddWithValue("account_id", accountId);
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
            return Results.Conflict(new { error = "challenge_already_consumed" });
    }

    if (!BasisDidProof.Verify(did, payload, signature))
    {
        await transaction.CommitAsync(cancellationToken);
        return Results.BadRequest(new { error = "invalid_did_signature" });
    }

    await using var link = new NpgsqlCommand("""
        INSERT INTO account_did_keys (account_id, did)
        VALUES (@account_id, @did)
        ON CONFLICT (did) DO NOTHING
        RETURNING did
        """, connection, transaction);
    link.Parameters.AddWithValue("account_id", accountId);
    link.Parameters.AddWithValue("did", did);
    var linked = await link.ExecuteScalarAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    if (linked is null)
        return Results.Conflict(new { error = "did_already_linked" });

    return Results.Ok(new { linked = true, did });
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


static bool TryGetAccountId(ClaimsPrincipal principal, out Guid accountId)
{
    accountId = Guid.Empty;
    if (principal.Identity?.IsAuthenticated != true)
        return false;
    var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(subject, out accountId);
}

public partial class Program { }

public sealed record CreateDidLinkChallengeRequest(string Did);
public sealed record CompleteDidLinkChallengeRequest(string Signature);
