using Microsoft.AspNetCore.Identity;

namespace Klaimin.Core;

public class AppUser : IdentityUser
{
    public string? ManagerId { get; set; }
    public AppUser? Manager { get; set; }
}

public static class Roles
{
    public const string Claimant = "claimant";
    public const string Manager = "manager";
    public const string Finance = "finance";
    public const string Admin = "admin";

    public static readonly string[] All = [Claimant, Manager, Finance, Admin];
}
