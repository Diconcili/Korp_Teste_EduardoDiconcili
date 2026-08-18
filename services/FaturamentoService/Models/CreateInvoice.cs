namespace FaturamentoService.Models;
public record CreateInvoice(List<InvoiceItem>? Items, string? IdempotencyKey);
