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
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>().HasQueryFilter(q => !q.IsDeleted);

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

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Email).IsUnique();
            b.HasData(new User
            {
                Id = 1,
                Email = "test@example.com",
                PasswordHash = "$2a$11$iWog2Xui8CDiamZd.HXvYeFqEiGoyGZskgF3nP2vRCiHWz865dT7S"
            });
        });
    }
}