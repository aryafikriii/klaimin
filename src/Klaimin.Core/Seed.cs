using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

public static class Seed
{
    public static string EmailFor(string name) => $"{name}@klaimin.test";

    /// <summary>
    /// The seeded accounts by name and role. Finance has two, so that a finance user's own claim has someone to decide it.
    /// </summary>
    public static readonly (string Name, string Role)[] Accounts =
    [
        (Roles.Claimant, Roles.Claimant),
        (Roles.Manager, Roles.Manager),
        (Roles.Finance, Roles.Finance),
        ("finance2", Roles.Finance),
        (Roles.Admin, Roles.Admin),
    ];

    /// <summary>
    /// Brings the database up to date and adds whatever seeded categories, roles, and accounts are missing.
    /// With no password the accounts cannot sign in with one, which leaves the development sign-in buttons as the only way in.
    /// </summary>
    public static async Task RunAsync(
        KlaiminDb db, UserManager<AppUser> users, RoleManager<IdentityRole> roles, string? password)
    {
        await db.Database.MigrateAsync();

        if (!await db.Categories.AnyAsync())
        {
            db.Categories.AddRange(
                new Category { Name = "Meals", Cap = 150_000 },
                new Category { Name = "Transport", Cap = 300_000 },
                new Category { Name = "Lodging", Cap = 1_000_000 },
                new Category { Name = "Office supplies", Cap = 500_000 },
                new Category { Name = "Other", Cap = 250_000 });
            await db.SaveChangesAsync();
        }

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)));
        }

        foreach (var (name, role) in Accounts)
        {
            if (await users.FindByEmailAsync(EmailFor(name)) is not null) continue;

            var user = new AppUser { UserName = EmailFor(name), Email = EmailFor(name), EmailConfirmed = true };
            Check(password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role));
        }

        var claimant = (await users.FindByEmailAsync(EmailFor(Roles.Claimant)))!;
        if (claimant.ManagerId is null)
        {
            claimant.ManagerId = (await users.FindByEmailAsync(EmailFor(Roles.Manager)))!.Id;
            Check(await users.UpdateAsync(claimant));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Seeding failed: " + string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
