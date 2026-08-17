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

    static async Task<TestBillingDatabase> CreateDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"korp-billing-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<BillingDb>().UseSqlite($"Data Source={path}").Options;
        await using var db = new BillingDb(options);
        await db.Database.EnsureCreatedAsync();
        return new(options, path);
    }

    sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    sealed class ThrowingHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("Serviço indisponível."); }
    sealed class TestBillingDatabase(DbContextOptions<BillingDb> options, string path) : IDisposable { public DbContextOptions<BillingDb> Options { get; } = options; public void Dispose() { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } } }
}
