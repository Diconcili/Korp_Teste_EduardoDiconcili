using FaturamentoService.Models;
namespace FaturamentoService.Services;
public record InvoiceCreationResult(InvoiceView Invoice, bool Created);
