namespace EstoqueService.Models;
public record ConsumeStock(List<StockItem> Items, string? OperationId = null);
