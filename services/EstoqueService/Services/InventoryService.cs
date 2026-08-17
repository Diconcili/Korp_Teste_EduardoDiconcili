using EstoqueService.Data;
using EstoqueService.Models;
using Microsoft.EntityFrameworkCore;

namespace EstoqueService.Services;

public class InventoryService(StockDb db)
{
    public async Task<StockConsumptionResult> ConsumeAsync(ConsumeStock request)
    {
        var items = request.Items.GroupBy(item => item.ProductId).Select(group => new StockItem(group.Key, group.Sum(item => item.Quantity))).ToList();
        if (items.Count == 0 || items.Any(item => item.Quantity <= 0)) return new(false, "Itens de estoque inválidos.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var operationId = request.OperationId?.Trim();
        if (!string.IsNullOrWhiteSpace(operationId) && await db.StockConsumptions.AnyAsync(consumption => consumption.OperationId == operationId))
            return new(true, "Estoque já atualizado para esta nota.");
        foreach (var item in items)
        {
            var changed = await db.Products.Where(product => product.Id == item.ProductId && product.Balance >= item.Quantity)
                .ExecuteUpdateAsync(update => update.SetProperty(product => product.Balance, product => product.Balance - item.Quantity));
            if (changed == 1) continue;

            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == item.ProductId);
            await transaction.RollbackAsync();
                return new(false, product is null ? $"Produto {item.ProductId} não encontrado." : $"Estoque insuficiente para {product.Description}.");
        }
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            db.StockConsumptions.Add(new StockConsumption { OperationId = operationId });
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return new(true, "Estoque atualizado.");
    }
}
