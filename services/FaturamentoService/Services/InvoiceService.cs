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
            var response = await httpClientFactory.CreateClient("stock").PostAsJsonAsync("/api/stock/consume", new { items = view.Items, operationId = $"invoice-{number}" });
            if (!response.IsSuccessStatusCode)
            {
                var message = await GetStockFailureReasonAsync(response);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) return new(false, 409, message);
                await ScheduleRecoveryAsync(number, message);
                return new(false, 502, $"{message} A recuperação automática foi agendada.");
            }
            invoice.Status = "Fechada";
            invoice.EncryptedPayload = crypto.Encrypt(view with { Status = "Fechada" });
            db.StockRecoveryJobs.RemoveRange(db.StockRecoveryJobs.Where(job => job.InvoiceNumber == number));
            await db.SaveChangesAsync();
            return new(true, 200, "Nota impressa e fechada.");
        }
        catch (HttpRequestException)
        {
            await ScheduleRecoveryAsync(number, "Serviço de estoque indisponível.");
            return new(false, 503, "Serviço de estoque indisponível. A nota continua aberta e a recuperação automática foi agendada.");
        }
        catch (TaskCanceledException)
        {
            await ScheduleRecoveryAsync(number, "Tempo de resposta do serviço de estoque excedido.");
            return new(false, 503, "O serviço de estoque demorou para responder. A nota continua aberta e a recuperação automática foi agendada.");
        }
    }

    public async Task RemoveRecoveryAsync(int number)
    {
        var jobs = await db.StockRecoveryJobs.Where(job => job.InvoiceNumber == number).ToListAsync();
        if (jobs.Count == 0) return;
        db.StockRecoveryJobs.RemoveRange(jobs);
        await db.SaveChangesAsync();
    }

    async Task ScheduleRecoveryAsync(int number, string error)
    {
        var job = await db.StockRecoveryJobs.SingleOrDefaultAsync(item => item.InvoiceNumber == number);
        if (job is null)
        {
            job = new StockRecoveryJob { InvoiceNumber = number };
            db.StockRecoveryJobs.Add(job);
        }
        job.AttemptCount++;
        job.LastError = error;
        var seconds = Math.Min(900, 15 * Math.Pow(2, Math.Min(job.AttemptCount - 1, 6)));
        job.NextAttemptAt = DateTime.UtcNow.AddSeconds(seconds);
        await db.SaveChangesAsync();
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
