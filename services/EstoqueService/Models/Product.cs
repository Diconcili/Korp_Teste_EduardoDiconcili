namespace EstoqueService.Models;
public class Product { public int Id { get; set; } public string Code { get; set; } = ""; public string Description { get; set; } = ""; public int Balance { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
