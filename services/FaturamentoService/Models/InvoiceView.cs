namespace FaturamentoService.Models;

public record InvoiceView(int Number, string Status, List<InvoiceItem> Items, DateTime CreatedAt);
