using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

/// <summary>The policy values an admin manages: the categories with their caps, and the finance threshold.</summary>
public class PolicyService(KlaiminDb db)
{
    public const int MaxNameLength = 50;

    public Task<List<Category>> CategoriesAsync() => db.Categories.OrderBy(category => category.Id).ToListAsync();

    public Task<Category?> FindCategoryAsync(int id) => db.Categories.FirstOrDefaultAsync(category => category.Id == id);

    public async Task<Problem?> AddCategoryAsync(string? name, long cap)
    {
        var category = new Category();
        if (await SetAsync(category, name, cap) is { } problem) return problem;

        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>A new cap applies to receipts confirmed from now on. Flags already raised or not raised stay as they are.</summary>
    public async Task<Problem?> UpdateCategoryAsync(Category category, string? name, long cap)
    {
        if (await SetAsync(category, name, cap) is { } problem) return problem;

        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Categories are never deleted, so receipts that used one keep it. An inactive one is not offered for new receipts.</summary>
    public async Task SetActiveAsync(Category category, bool active)
    {
        category.IsActive = active;
        await db.SaveChangesAsync();
    }

    public Task<long> FinanceThresholdAsync() => db.Settings.Select(settings => settings.FinanceThreshold).SingleAsync();

    /// <summary>Applies to claims submitted from now on. A submitted claim carries the threshold it was submitted under.</summary>
    public async Task<Problem?> SetFinanceThresholdAsync(long threshold)
    {
        if (threshold <= 0) return new("FinanceThreshold", "The threshold must be more than Rp 0.");

        (await db.Settings.SingleAsync()).FinanceThreshold = threshold;
        await db.SaveChangesAsync();
        return null;
    }

    private async Task<Problem?> SetAsync(Category category, string? name, long cap)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return new("Name", "Give the category a name.");
        if (name.Length > MaxNameLength) return new("Name", $"Keep the name to {MaxNameLength} characters.");
        if (await db.Categories.AnyAsync(other => other.Id != category.Id && other.Name.ToLower() == name.ToLower()))
            return new("Name", $"There is already a category called {name}.");
        if (cap <= 0) return new("Cap", "The cap must be more than Rp 0.");

        category.Name = name;
        category.Cap = cap;
        return null;
    }
}
