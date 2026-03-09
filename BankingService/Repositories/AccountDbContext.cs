using BankingService.Models;
using Microsoft.EntityFrameworkCore;

namespace BankingService.Services;

public sealed class AccountDbContext : DbContext
{
    public AccountDbContext(DbContextOptions<AccountDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);

            // int PK — database generates the value on insert.
            e.Property(a => a.Id)
                .ValueGeneratedOnAdd();

            e.Property(a => a.Balance)
                .HasColumnType("decimal(18,4)");

            e.Property(a => a.Type)
                .HasConversion<string>();

            // Optimistic concurrency — EF Core automatically checks this token
            // on every UPDATE and throws DbUpdateConcurrencyException if it has
            // changed since the row was read.
            e.Property(a => a.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        });
    }
}