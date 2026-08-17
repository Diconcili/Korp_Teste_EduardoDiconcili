using EstoqueService.Models;
using Microsoft.EntityFrameworkCore;
namespace EstoqueService.Data;
public class StockDb(DbContextOptions<StockDb> options) : DbContext(options) { public DbSet<Product> Products => Set<Product>(); protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Product>().HasIndex(product => product.Code).IsUnique(); }
