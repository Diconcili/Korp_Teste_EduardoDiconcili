using FaturamentoService.Models;
using Microsoft.EntityFrameworkCore;
namespace FaturamentoService.Data;

public class BillingDb(DbContextOptions<BillingDb> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<User> Users => Set<User>();
    public DbSet<MfaChallenge> Challenges => Set<MfaChallenge>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<StockRecoveryJob> StockRecoveryJobs => Set<StockRecoveryJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Invoice>().HasIndex(invoice => invoice.Number).IsUnique();
        builder.Entity<Invoice>().HasIndex(invoice => invoice.IdempotencyKey).IsUnique();
        builder.Entity<User>().HasIndex(user => user.Username).IsUnique();
        builder.Entity<Session>().HasIndex(session => session.Token).IsUnique();
        builder.Entity<StockRecoveryJob>().HasIndex(job => job.InvoiceNumber).IsUnique();
    }
}
