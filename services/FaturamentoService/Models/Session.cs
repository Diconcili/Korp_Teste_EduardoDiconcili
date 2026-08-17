namespace FaturamentoService.Models;
public class Session { public int Id { get; set; } public string Token { get; set; } = ""; public int UserId { get; set; } public DateTime ExpiresAt { get; set; } }
