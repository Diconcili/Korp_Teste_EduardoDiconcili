using System.Security.Cryptography;
using FaturamentoService.Data;
using FaturamentoService.Models;
using FaturamentoService.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "faturamento.db");
var legacyDatabasePath = Path.Combine(builder.Environment.ContentRootPath, "faturamento.db");
if (!File.Exists(databasePath) && File.Exists(legacyDatabasePath)) File.Move(legacyDatabasePath, databasePath);
var encryptionSecret = Environment.GetEnvironmentVariable("KORP_ENCRYPTION_KEY");
if (string.IsNullOrWhiteSpace(encryptionSecret) || encryptionSecret.Length < 32)
    throw new InvalidOperationException("Defina KORP_ENCRYPTION_KEY com ao menos 32 caracteres antes de iniciar o FaturamentoService.");
var authSigningSecret = Environment.GetEnvironmentVariable("KORP_AUTH_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(authSigningSecret) || authSigningSecret.Length < 32)
    throw new InvalidOperationException("Defina KORP_AUTH_SIGNING_KEY com ao menos 32 caracteres nos dois serviços.");
var stockServiceKey = Environment.GetEnvironmentVariable("KORP_STOCK_SERVICE_KEY");
if (string.IsNullOrWhiteSpace(stockServiceKey) || stockServiceKey.Length < 32)
    throw new InvalidOperationException("Defina KORP_STOCK_SERVICE_KEY com ao menos 32 caracteres nos dois serviços.");
var bootstrapUsername = Environment.GetEnvironmentVariable("KORP_BOOTSTRAP_ADMIN_USERNAME")?.Trim();
var bootstrapPassword = Environment.GetEnvironmentVariable("KORP_BOOTSTRAP_ADMIN_PASSWORD");
var bootstrapTotpSecret = Environment.GetEnvironmentVariable("KORP_BOOTSTRAP_ADMIN_TOTP_SECRET")?.Trim();

builder.Services.AddDbContext<BillingDb>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddHttpClient("stock", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["StockServiceUrl"] ?? "http://localhost:5101");
    client.DefaultRequestHeaders.Add("X-Korp-Service-Key", stockServiceKey);
});
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddHostedService<StockRecoveryWorker>();
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(new CryptoService(encryptionSecret));
builder.Services.AddSingleton(new SessionTokenService(authSigningSecret));
builder.Services.AddSingleton<AuthenticationAttemptGuard>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS StockRecoveryJobs (Id INTEGER NOT NULL CONSTRAINT PK_StockRecoveryJobs PRIMARY KEY AUTOINCREMENT, InvoiceNumber INTEGER NOT NULL, AttemptCount INTEGER NOT NULL, NextAttemptAt TEXT NOT NULL, LastError TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
    await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_StockRecoveryJobs_InvoiceNumber ON StockRecoveryJobs (InvoiceNumber)");
    if (!await db.Users.AnyAsync())
    {
        if (string.IsNullOrWhiteSpace(bootstrapUsername) || string.IsNullOrWhiteSpace(bootstrapPassword) || bootstrapPassword.Length < 12 || !Totp.IsValidSecret(bootstrapTotpSecret))
            throw new InvalidOperationException("Banco sem usuários. Defina KORP_BOOTSTRAP_ADMIN_USERNAME, KORP_BOOTSTRAP_ADMIN_PASSWORD (mínimo de 12 caracteres) e KORP_BOOTSTRAP_ADMIN_TOTP_SECRET (Base32 válido).");
        var salt = RandomNumberGenerator.GetBytes(16);
        db.Users.Add(new User { Username = bootstrapUsername!, PasswordSalt = Convert.ToBase64String(salt), PasswordHash = Passwords.Hash(bootstrapPassword!, salt), TotpSecret = bootstrapTotpSecret! });
        await db.SaveChangesAsync();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "faturamento" }));
app.MapPost("/api/auth/login", async (Login input, BillingDb db, AuthenticationAttemptGuard attempts, HttpRequest request) =>
{
    var normalizedUsername = input.Username?.Trim().ToUpperInvariant() ?? "";
    var attemptKey = $"login:{request.HttpContext.Connection.RemoteIpAddress}:{normalizedUsername}";
    if (attempts.IsBlocked(attemptKey)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    var user = string.IsNullOrWhiteSpace(normalizedUsername) ? null : await db.Users.SingleOrDefaultAsync(item => item.Username == input.Username);
    if (user is null || string.IsNullOrEmpty(input.Password) || !Passwords.Verify(input.Password, user))
    {
        if (attempts.RegisterFailure(attemptKey)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        return Results.Unauthorized();
    }
    attempts.Clear(attemptKey);
    var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    db.Challenges.Add(new MfaChallenge { Token = challenge, UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
    await db.SaveChangesAsync();
    return Results.Ok(new { challenge, expiresInSeconds = 300, mfaRequired = true });
}).RequireRateLimiting("authentication");
app.MapPost("/api/auth/mfa", async (Mfa input, BillingDb db, SessionTokenService tokens, AuthenticationAttemptGuard attempts) =>
{
    var attemptKey = $"mfa:{input.Challenge}";
    if (attempts.IsBlocked(attemptKey)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    var challenge = await db.Challenges.Include(item => item.User).SingleOrDefaultAsync(item => item.Token == input.Challenge && item.ExpiresAt > DateTime.UtcNow && !item.Used);
    if (challenge is null || !Totp.IsValid(challenge.User.TotpSecret, input.Code))
    {
        if (attempts.RegisterFailure(attemptKey))
        {
            if (challenge is not null) { challenge.Used = true; await db.SaveChangesAsync(); }
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        return Results.Unauthorized();
    }
    attempts.Clear(attemptKey);
    challenge.Used = true;
    var expiresAt = DateTime.UtcNow.AddMinutes(30);
    var token = tokens.Create(expiresAt);
    db.Sessions.Add(new Session { Token = token, UserId = challenge.UserId, ExpiresAt = expiresAt });
    await db.SaveChangesAsync();
    return Results.Ok(new { token, expiresAt });
}).RequireRateLimiting("authentication");
app.MapDelete("/api/auth/session", async (HttpRequest request, BillingDb db) =>
{
    var token = Auth.GetToken(request);
    if (token is null) return Results.Unauthorized();
    var session = await db.Sessions.SingleOrDefaultAsync(item => item.Token == token && item.ExpiresAt > DateTime.UtcNow);
    if (session is null) return Results.Unauthorized();
    db.Sessions.Remove(session);
    await db.SaveChangesAsync();
    return Results.NoContent();
});
app.MapGet("/api/invoices", async (HttpRequest request, BillingDb db, InvoiceService invoices, int? page, int? pageSize) =>
{
    if (!await Auth.Valid(request, db)) return Results.Unauthorized();
    var requestedPage = Math.Max(1, page ?? 1);
    var requestedPageSize = Math.Clamp(pageSize ?? 10, 1, 50);
    return Results.Ok(await invoices.ListAsync(requestedPage, requestedPageSize));
});
app.MapPost("/api/invoices", async (CreateInvoice? input, BillingDb db, InvoiceService invoices, HttpRequest request) =>
{
    if (!await Auth.Valid(request, db)) return Results.Unauthorized();
    if (input?.Items is not { Count: > 0 } items || items.Any(item => item.Quantity <= 0)) return Results.BadRequest(new { message = "Inclua produtos com quantidades válidas." });
    var result = await invoices.CreateAsync(input);
    return result.Created ? Results.Created($"/api/invoices/{result.Invoice.Number}", result.Invoice) : Results.Ok(result.Invoice);
});
app.MapPost("/api/invoices/{number:int}/print", async (int number, BillingDb db, InvoiceService invoices, HttpRequest request) =>
{
    if (!await Auth.Valid(request, db)) return Results.Unauthorized();
    var result = await invoices.PrintAsync(number);
    return result.Success ? Results.Ok(new { message = result.Message }) : Results.Problem(result.Message, statusCode: result.StatusCode);
});
app.Run();
