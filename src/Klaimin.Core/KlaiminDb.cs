using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

public class KlaiminDb(DbContextOptions<KlaiminDb> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Claim>().Property(claim => claim.Status).HasConversion<string>();
        builder.Entity<Category>().HasIndex(category => category.Name).IsUnique();
    }
}
