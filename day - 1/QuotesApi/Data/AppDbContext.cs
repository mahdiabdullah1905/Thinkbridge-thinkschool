using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Collection>(b =>
        {
            b.HasKey(c => c.Id);
            
            b.OwnsMany(c => c.Items, ib =>
            {
                ib.WithOwner().HasForeignKey("CollectionId");
                ib.HasKey("Id");
                ib.Property<int>("Id").ValueGeneratedOnAdd();
            });
        });
    }
}