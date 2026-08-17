using FaturamentoService.Data;
using FaturamentoService.Models;
using Microsoft.EntityFrameworkCore;

namespace FaturamentoService.Services;

public class InvoiceService(BillingDb db, CryptoService crypto, IHttpClientFactory httpClientFactory)
{
    public async Task<InvoiceCreationResult> CreateAsync(CreateInvoice input)
    {
        var key = input.IdempotencyKey ?? Guid.NewGuid().ToString();
        var existing = await db.Invoices.SingleOrDefaultAsync(invoice => invoice.IdempotencyKey == key);
        if (existing is not null) return new(crypto.Decrypt<InvoiceView>(existing.EncryptedPayload)!, false);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var number = (await db.Invoices.MaxAsync(invoice => (int?)invoice.Number) ?? 0) + 1;
            var view = new InvoiceView(number, "Aberta", input.Items, DateTime.UtcNow);
            db.Invoices.Add(new Invoice { Number = number, Status = "Aberta", EncryptedPayload = crypto.Encrypt(view), IdempotencyKey = key });
            try
            {
                await db.SaveChangesAsync();
                return new(view, true);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                existing = await db.Invoices.SingleOrDefaultAsync(invoice => invoice.IdempotencyKey == key);
                if (existing is not null) return new(crypto.Decrypt<InvoiceView>(existing.EncryptedPayload)!, false);
            }
        }
        throw new InvalidOperationException("Não foi possível gerar uma numeração sequencial para a nota.");
    }

    public async Task<InvoicePrintResult> PrintAsync(int number)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(item => item.Number == number);
        if (invoice is null) return new(false, 404, "Nota fiscal não encontrada.");
        if (invoice.Status != "Aberta") return new(false, 409, "Apenas notas abertas podem ser impressas.");
        var view = crypto.Decrypt<InvoiceView>(invoice.EncryptedPayload)!;

        try
        {
            var response = await httpClientFactory.CreateClient("stock").PostAsJsonAsync("/api/stock/consume", new { items = view.Items });
            if (!response.IsSuccessStatusCode) return new(false, response.StatusCode == System.Net.HttpStatusCode.Conflict ? 409 : 502, await GetStockFailureReasonAsync(response));
            invoice.Status = "Fechada";
            invoice.EncryptedPayload = crypto.Encrypt(view with { Status = "Fechada" });
            await db.SaveChangesAsync();
            return new(true, 200, "Nota impressa e fechada.");
        }
        catch (HttpRequestException)
        {
            return new(false, 503, "Serviço de estoque indisponível. Tente novamente; a nota continua aberta.");
        }
    }

    static async Task<string> GetStockFailureReasonAsync(HttpResponseMessage response)
    {
        const string fallback = "O estoque não pôde ser atualizado; a nota continua aberta.";
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? fallback : fallback;
        }
        catch (System.Text.Json.JsonException) { return fallback; }
    }
}
