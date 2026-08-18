using FaturamentoService.Data;
using FaturamentoService.Models;
using FaturamentoService.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Korp.IntegrationTests;

public class InvoiceServiceTests
{
    const string TestEncryptionKey = "chave-exclusiva-para-testes-automatizados-korp-2026";

    [Fact]
    public async Task CreateAsync_WithTheSameIdempotencyKey_CreatesOnlyOneInvoice()
    {
        using var database = await CreateDatabaseAsync();
        await using var db = new BillingDb(database.Options);
        var service = new InvoiceService(db, new CryptoService(TestEncryptionKey), new FakeHttpClientFactory(new HttpClient()));
        var request = new CreateInvoice([new InvoiceItem(1, 2)], "same-request");

        var first = await service.CreateAsync(request);
        var repeated = await service.CreateAsync(request);

        Assert.True(first.Created);
        Assert.False(repeated.Created);
        Assert.Equal(first.Invoice.Number, repeated.Invoice.Number);
        Assert.Equal(1, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task PrintAsync_WhenStockServiceIsUnavailable_KeepsInvoiceOpen()
    {
        using var database = await CreateDatabaseAsync();
        await using var db = new BillingDb(database.Options);
        var client = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://stock") };
        var service = new InvoiceService(db, new CryptoService(TestEncryptionKey), new FakeHttpClientFactory(client));
        var invoice = await service.CreateAsync(new CreateInvoice([new InvoiceItem(1, 1)], "outage-request"));

        var result = await service.PrintAsync(invoice.Invoice.Number);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("Aberta", await db.Invoices.Where(item => item.Number == invoice.Invoice.Number).Select(item => item.Status).SingleAsync());
        var recovery = await db.StockRecoveryJobs.SingleAsync(item => item.InvoiceNumber == invoice.Invoice.Number);
        Assert.Equal(1, recovery.AttemptCount);
        Assert.True(recovery.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyRequestedPageInDescendingOrder()
    {
        using var database = await CreateDatabaseAsync();
        await using var db = new BillingDb(database.Options);
        var crypto = new CryptoService(TestEncryptionKey);
        for (var number = 1; number <= 12; number++)
        {
            var view = new InvoiceView(number, "Aberta", [new InvoiceItem(1, 1)], DateTime.UtcNow);
            db.Invoices.Add(new Invoice { Number = number, Status = "Aberta", CreatedAt = view.CreatedAt, IdempotencyKey = $"page-{number}", EncryptedPayload = crypto.Encrypt(view) });
        }
        await db.SaveChangesAsync();
        var service = new InvoiceService(db, crypto, new FakeHttpClientFactory(new HttpClient()));

        var result = await service.ListAsync(new InvoiceQuery { Page = 2, PageSize = 5, SortBy = "number", SortDirection = "desc" });

        Assert.Equal(12, result.Total);
        Assert.Equal([7, 6, 5, 4, 3], result.Items.Select(invoice => invoice.Number));
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
    }

    [Fact]
    public async Task ListAsync_AppliesStatusProductAndOrderingFiltersTogether()
    {
        using var database = await CreateDatabaseAsync();
        await using var db = new BillingDb(database.Options);
        var crypto = new CryptoService(TestEncryptionKey);
        var views = new[]
        {
            new InvoiceView(1, "Aberta", [new InvoiceItem(10, 1)], new DateTime(2026, 8, 10)),
            new InvoiceView(2, "Fechada", [new InvoiceItem(10, 1)], new DateTime(2026, 8, 11)),
            new InvoiceView(3, "Fechada", [new InvoiceItem(11, 1)], new DateTime(2026, 8, 12)),
            new InvoiceView(4, "Fechada", [new InvoiceItem(10, 1)], new DateTime(2026, 8, 9)),
        };
        foreach (var view in views)
        {
            db.Invoices.Add(new Invoice { Number = view.Number, Status = view.Status, CreatedAt = view.CreatedAt, IdempotencyKey = $"filter-{view.Number}", EncryptedPayload = crypto.Encrypt(view) });
            db.InvoiceProducts.AddRange(view.Items.Select(item => item.ProductId).Distinct().Select(productId => new InvoiceProduct { InvoiceNumber = view.Number, ProductId = productId }));
        }
        await db.SaveChangesAsync();
        var service = new InvoiceService(db, crypto, new FakeHttpClientFactory(new HttpClient()));

        var result = await service.ListAsync(new InvoiceQuery { Status = "Fechada", SortBy = "date", SortDirection = "asc", ProductId = 10 });

        Assert.Equal(2, result.Total);
        Assert.Equal([4, 2], result.Items.Select(invoice => invoice.Number));
    }

    [Fact]
    public async Task ListAsync_WhenRequestedPageExceedsFilteredResults_ReturnsLastAvailablePage()
    {
        using var database = await CreateDatabaseAsync();
        await using var db = new BillingDb(database.Options);
        var crypto = new CryptoService(TestEncryptionKey);
        var view = new InvoiceView(1, "Aberta", [new InvoiceItem(10, 1)], DateTime.UtcNow);
        db.Invoices.Add(new Invoice { Number = 1, Status = view.Status, CreatedAt = view.CreatedAt, IdempotencyKey = "last-page", EncryptedPayload = crypto.Encrypt(view) });
        await db.SaveChangesAsync();
        var service = new InvoiceService(db, crypto, new FakeHttpClientFactory(new HttpClient()));

        var result = await service.ListAsync(new InvoiceQuery { Page = 3, PageSize = 10, Status = "Aberta" });

        Assert.Equal(1, result.Page);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task DatabaseInitializer_BackfillsSearchMetadataFromLegacyInvoices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"korp-legacy-billing-{Guid.NewGuid():N}.db");
        try
        {
            var crypto = new CryptoService(TestEncryptionKey);
            var view = new InvoiceView(7, "Fechada", [new InvoiceItem(21, 1), new InvoiceItem(21, 2)], new DateTime(2026, 8, 18, 10, 30, 0, DateTimeKind.Utc));
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE Invoices (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Number INTEGER NOT NULL, Status TEXT NOT NULL, EncryptedPayload TEXT NOT NULL, IdempotencyKey TEXT NOT NULL);";
                await create.ExecuteNonQueryAsync();
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Invoices (Number, Status, EncryptedPayload, IdempotencyKey) VALUES (7, 'Fechada', $payload, 'legacy-7')";
                insert.Parameters.AddWithValue("$payload", crypto.Encrypt(view));
                await insert.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<BillingDb>().UseSqlite($"Data Source={path}").Options;
            await using var db = new BillingDb(options);

            await BillingDatabaseInitializer.InitializeAsync(db, crypto);

            Assert.Equal(view.CreatedAt, await db.Invoices.Where(invoice => invoice.Number == 7).Select(invoice => invoice.CreatedAt).SingleAsync());
            Assert.True(await db.InvoiceProducts.AnyAsync(item => item.InvoiceNumber == 7 && item.ProductId == 21));
            Assert.Equal(1, await db.InvoiceProducts.CountAsync());
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    static async Task<TestBillingDatabase> CreateDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"korp-billing-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<BillingDb>().UseSqlite($"Data Source={path}").Options;
        await using var db = new BillingDb(options);
        await db.Database.EnsureCreatedAsync();
        return new(options, path);
    }

    sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Serviço indisponível.");
    }

    sealed class TestBillingDatabase(DbContextOptions<BillingDb> options, string path) : IDisposable
    {
        public DbContextOptions<BillingDb> Options { get; } = options;

        public void Dispose()
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
        }
    }
}
