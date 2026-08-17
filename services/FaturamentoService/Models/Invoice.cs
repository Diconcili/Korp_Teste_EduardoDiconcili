namespace FaturamentoService.Models;
public class Invoice { public int Id { get; set; } public int Number { get; set; } public string Status { get; set; } = ""; public string EncryptedPayload { get; set; } = ""; public string IdempotencyKey { get; set; } = ""; }
