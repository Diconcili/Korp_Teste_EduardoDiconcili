using FaturamentoService.Data;
using Microsoft.EntityFrameworkCore;

namespace FaturamentoService.Services;

public class StockRecoveryWorker(IServiceScopeFactory scopeFactory, ILogger<StockRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RecoverDueInvoicesAsync(stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Falha ao processar a fila de recuperação de estoque."); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    async Task RecoverDueInvoicesAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
        var numbers = await db.StockRecoveryJobs
            .Where(job => job.NextAttemptAt <= DateTime.UtcNow)
            .OrderBy(job => job.NextAttemptAt)
            .Select(job => job.InvoiceNumber)
            .Take(20)
            .ToListAsync(stoppingToken);
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        foreach (var number in numbers)
        {
            var result = await invoices.PrintAsync(number);
            if (result.Success || result.StatusCode is 404 or 409) await invoices.RemoveRecoveryAsync(number);
        }
    }
}
