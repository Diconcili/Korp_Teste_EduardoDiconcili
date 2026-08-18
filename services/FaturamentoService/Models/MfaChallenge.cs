namespace FaturamentoService.Models;

public class MfaChallenge
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}
