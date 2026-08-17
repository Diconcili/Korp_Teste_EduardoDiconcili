using System.Security.Cryptography;
using FaturamentoService.Data;
using FaturamentoService.Models;
using FaturamentoService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "faturamento.db");
var legacyDatabasePath = Path.Combine(builder.Environment.ContentRootPath, "faturamento.db");
if (!File.Exists(databasePath) && File.Exists(legacyDatabasePath)) File.Move(legacyDatabasePath, databasePath);
var encryptionSecret = Environment.GetEnvironmentVariable("KORP_ENCRYPTION_KEY");
if (string.IsNullOrWhiteSpace(encryptionSecret) || encryptionSecret.Length < 32)
    throw new InvalidOperationException("Defina KORP_ENCRYPTION_KEY com ao menos 32 caracteres antes de iniciar o FaturamentoService.");

builder.Services.AddDbContext<BillingDb>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddHttpClient("stock", client => client.BaseAddress = new Uri(builder.Configuration["StockServiceUrl"] ?? "http://localhost:5101"));
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddHostedService<StockRecoveryWorker>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(new CryptoService(encryptionSecret));
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS StockRecoveryJobs (Id INTEGER NOT NULL CONSTRAINT PK_StockRecoveryJobs PRIMARY KEY AUTOINCREMENT, InvoiceNumber INTEGER NOT NULL, AttemptCount INTEGER NOT NULL, NextAttemptAt TEXT NOT NULL, LastError TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
    await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_StockRecoveryJobs_InvoiceNumber ON StockRecoveryJobs (InvoiceNumber)");
    if (!await db.Users.AnyAsync())
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        db.Users.Add(new User { Username = "admin", PasswordSalt = Convert.ToBase64String(salt), PasswordHash = Passwords.Hash("Temp123!", salt), TotpSecret = "JBSWY3DPEHPK3PXP" });
        await db.SaveChangesAsync();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "faturamento" }));
app.MapPost("/api/auth/login", async (Login input, BillingDb db) =>
{
    var user = await db.Users.SingleOrDefaultAsync(item => item.Username == input.Username);
    if (user is null || !Passwords.Verify(input.Password, user)) return Results.Unauthorized();
    var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    db.Challenges.Add(new MfaChallenge { Token = challenge, UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
    await db.SaveChangesAsync();
    return Results.Ok(new { challenge, expiresInSeconds = 300, mfaRequired = true });
});
app.MapPost("/api/auth/mfa", async (Mfa input, BillingDb db) =>
{
    var challenge = await db.Challenges.Include(item => item.User).SingleOrDefaultAsync(item => item.Token == input.Challenge && item.ExpiresAt > DateTime.UtcNow && !item.Used);
    if (challenge is null || !Totp.IsValid(challenge.User.TotpSecret, input.Code)) return Results.Unauthorized();
    challenge.Used = true;
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var expiresAt = DateTime.UtcNow.AddMinutes(30);
    db.Sessions.Add(new Session { Token = token, UserId = challenge.UserId, ExpiresAt = expiresAt });
    await db.SaveChangesAsync();
    return Results.Ok(new { token, expiresAt });
});
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
app.MapGet("/api/invoices", async (BillingDb db, CryptoService crypto) => (await db.Invoices.OrderByDescending(item => item.Number).ToListAsync()).Select(item => crypto.Decrypt<InvoiceView>(item.EncryptedPayload)!));
app.MapPost("/api/invoices", async (CreateInvoice input, BillingDb db, InvoiceService invoices, HttpRequest request) =>
{
    if (!await Auth.Valid(request, db)) return Results.Unauthorized();
    if (input.Items.Count == 0 || input.Items.Any(item => item.Quantity <= 0)) return Results.BadRequest(new { message = "Inclua produtos com quantidades válidas." });
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
