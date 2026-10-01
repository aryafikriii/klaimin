using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

public class KlaiminDb(DbContextOptions<KlaiminDb> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Settings> Settings => Set<Settings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Claim>().Property(claim => claim.Status).HasConversion<string>();
        builder.Entity<Decision>().Property(decision => decision.Step).HasConversion<string>();
        builder.Entity<Decision>().Property(decision => decision.Kind).HasConversion<string>();
        builder.Entity<Settings>().HasData(new Settings { Id = 1, FinanceThreshold = Klaimin.Core.Settings.SeededFinanceThreshold });
        builder.Entity<Category>().HasIndex(category => category.Name).IsUnique();
    }
}
