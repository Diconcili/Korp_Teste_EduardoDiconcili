namespace EstoqueService.Models;

public class StockConsumption
{
    public int Id { get; set; }
    public string OperationId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
