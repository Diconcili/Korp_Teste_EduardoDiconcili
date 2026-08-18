using FaturamentoService.Models;
using FaturamentoService.Services;
using Microsoft.EntityFrameworkCore;

namespace FaturamentoService.Data;

public static class BillingDatabaseInitializer
{
    public static async Task InitializeAsync(BillingDb db, CryptoService crypto)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS StockRecoveryJobs (Id INTEGER NOT NULL CONSTRAINT PK_StockRecoveryJobs PRIMARY KEY AUTOINCREMENT, InvoiceNumber INTEGER NOT NULL, AttemptCount INTEGER NOT NULL, NextAttemptAt TEXT NOT NULL, LastError TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_StockRecoveryJobs_InvoiceNumber ON StockRecoveryJobs (InvoiceNumber)");
        await EnsureInvoiceCreatedAtAsync(db);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS InvoiceProducts (InvoiceNumber INTEGER NOT NULL, ProductId INTEGER NOT NULL, CONSTRAINT PK_InvoiceProducts PRIMARY KEY (InvoiceNumber, ProductId))");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_InvoiceProducts_ProductId ON InvoiceProducts (ProductId)");
        await BackfillInvoiceMetadataAsync(db, crypto);
    }

    static async Task EnsureInvoiceCreatedAtAsync(BillingDb db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('Invoices')";
        await using var reader = await command.ExecuteReaderAsync();
        var hasColumn = false;
        while (await reader.ReadAsync())
            if (string.Equals(reader.GetString(1), "CreatedAt", StringComparison.OrdinalIgnoreCase)) hasColumn = true;
        await reader.DisposeAsync();
        if (!hasColumn)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Invoices ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
    }

    static async Task BackfillInvoiceMetadataAsync(BillingDb db, CryptoService crypto)
    {
        var invoices = await db.Invoices
            .Where(invoice => invoice.CreatedAt == default || !db.InvoiceProducts.Any(item => item.InvoiceNumber == invoice.Number))
            .ToListAsync();
        if (invoices.Count == 0) return;
        var invoiceNumbers = invoices.Select(invoice => invoice.Number).ToList();
        var existingLinks = await db.InvoiceProducts
            .Where(item => invoiceNumbers.Contains(item.InvoiceNumber))
            .Select(item => new { item.InvoiceNumber, item.ProductId })
            .ToListAsync();
        var links = existingLinks.Select(item => (item.InvoiceNumber, item.ProductId)).ToHashSet();
        var changed = false;
        foreach (var invoice in invoices)
        {
            var view = crypto.Decrypt<InvoiceView>(invoice.EncryptedPayload)!;
            if (invoice.CreatedAt == default) { invoice.CreatedAt = view.CreatedAt; changed = true; }
            foreach (var productId in view.Items.Select(item => item.ProductId).Distinct())
                if (links.Add((invoice.Number, productId))) { db.InvoiceProducts.Add(new InvoiceProduct { InvoiceNumber = invoice.Number, ProductId = productId }); changed = true; }
        }
        if (changed) await db.SaveChangesAsync();
    }
}
