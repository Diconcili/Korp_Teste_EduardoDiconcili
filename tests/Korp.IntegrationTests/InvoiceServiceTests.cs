using FaturamentoService.Data;
using FaturamentoService.Models;
using FaturamentoService.Services;
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
            db.Invoices.Add(new Invoice { Number = number, Status = "Aberta", IdempotencyKey = $"page-{number}", EncryptedPayload = crypto.Encrypt(view) });
        }
        await db.SaveChangesAsync();
        var service = new InvoiceService(db, crypto, new FakeHttpClientFactory(new HttpClient()));

        var result = await service.ListAsync(2, 5);

        Assert.Equal(12, result.Total);
        Assert.Equal([7, 6, 5, 4, 3], result.Items.Select(invoice => invoice.Number));
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
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
