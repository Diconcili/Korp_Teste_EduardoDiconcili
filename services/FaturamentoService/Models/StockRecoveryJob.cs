namespace FaturamentoService.Models;

public class StockRecoveryJob
{
    public int Id { get; set; }
    public int InvoiceNumber { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string LastError { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
