using EstoqueService.Data;
using EstoqueService.Models;
using EstoqueService.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Korp.IntegrationTests;

public class InventoryServiceTests
{
    [Fact]
    public async Task ConsumeAsync_WhenBalanceIsInsufficient_DoesNotChangeBalance()
    {
        using var database = await CreateDatabaseAsync(1);
        await using var db = new StockDb(database.Options);
        var result = await new InventoryService(db).ConsumeAsync(new ConsumeStock([new StockItem(1, 2)]));

        Assert.False(result.Success);
        Assert.Contains("Estoque insuficiente", result.Message);
        Assert.Equal(1, await db.Products.Where(product => product.Id == 1).Select(product => product.Balance).SingleAsync());
    }

    [Fact]
    public async Task ConsumeAsync_WhenTwoRequestsCompeteForLastUnit_AllowsOnlyOne()
    {
        using var database = await CreateDatabaseAsync(1);
        await using var firstDb = new StockDb(database.Options);
        await using var secondDb = new StockDb(database.Options);

        var results = await Task.WhenAll(
            new InventoryService(firstDb).ConsumeAsync(new ConsumeStock([new StockItem(1, 1)])),
            new InventoryService(secondDb).ConsumeAsync(new ConsumeStock([new StockItem(1, 1)])));

        Assert.Single(results, result => result.Success);
        await using var verificationDb = new StockDb(database.Options);
        Assert.Equal(0, await verificationDb.Products.Where(product => product.Id == 1).Select(product => product.Balance).SingleAsync());
    }

    [Fact]
    public async Task ConsumeAsync_WithTheSameOperationId_DeductsStockOnlyOnce()
    {
        using var database = await CreateDatabaseAsync(2);
        await using var db = new StockDb(database.Options);
        var service = new InventoryService(db);

        var first = await service.ConsumeAsync(new ConsumeStock([new StockItem(1, 1)], "invoice-1"));
        var repeated = await service.ConsumeAsync(new ConsumeStock([new StockItem(1, 1)], "invoice-1"));

        Assert.True(first.Success);
        Assert.True(repeated.Success);
        Assert.Equal(1, await db.Products.Where(product => product.Id == 1).Select(product => product.Balance).SingleAsync());
        Assert.Equal(1, await db.StockConsumptions.CountAsync());
    }

    static async Task<TestStockDatabase> CreateDatabaseAsync(int balance)
    {
        var path = Path.Combine(Path.GetTempPath(), $"korp-stock-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<StockDb>().UseSqlite($"Data Source={path};Default Timeout=10").Options;
        await using var db = new StockDb(options);
        await db.Database.EnsureCreatedAsync();
        db.Products.Add(new Product { Id = 1, Code = "SKU-1", Description = "Produto de teste", Balance = balance });
        await db.SaveChangesAsync();
        return new(options, path);
    }

    sealed class TestStockDatabase(DbContextOptions<StockDb> options, string path) : IDisposable
    {
        public DbContextOptions<StockDb> Options { get; } = options;
        public void Dispose() { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
