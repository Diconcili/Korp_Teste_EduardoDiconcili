namespace FaturamentoService.Models;

public sealed class InvoiceQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Status { get; init; }
    public string SortBy { get; init; } = "date";
    public string SortDirection { get; init; } = "desc";
    public int? ProductId { get; init; }

    public bool IsValid =>
        (Status is null or "Aberta" or "Fechada") &&
        SortBy is "number" or "date" &&
        SortDirection is "asc" or "desc" &&
        ProductId is null or > 0;
}
