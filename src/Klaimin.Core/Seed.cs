using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

public static class Seed
{
    public static string EmailFor(string role) => $"{role}@klaimin.test";

    /// <summary>
    /// Creates the database and one account per role. With no password the accounts cannot sign in
    /// with one, which leaves the development sign-in buttons as the only way in.
    /// </summary>
    public static async Task RunAsync(
        KlaiminDb db, UserManager<AppUser> users, RoleManager<IdentityRole> roles, string? password)
    {
        // ponytail: EnsureCreated cannot change an existing schema. Move to migrations once a database is worth keeping.
        await db.Database.EnsureCreatedAsync();
        if (await users.Users.AnyAsync()) return;

        foreach (var role in Roles.All)
        {
            Check(await roles.CreateAsync(new IdentityRole(role)));
            var user = new AppUser { UserName = EmailFor(role), Email = EmailFor(role), EmailConfirmed = true };
            Check(password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role));
        }

        var claimant = (await users.FindByEmailAsync(EmailFor(Roles.Claimant)))!;
        claimant.ManagerId = (await users.FindByEmailAsync(EmailFor(Roles.Manager)))!.Id;
        Check(await users.UpdateAsync(claimant));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Seeding failed: " + string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
