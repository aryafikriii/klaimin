using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

/// <summary>What the claimant confirmed for one receipt.</summary>
public record ReceiptEntry(long Total, DateOnly Date, int CategoryId, IReadOnlyList<LineItem> LineItems);

/// <summary>Why a change to a claim was refused, and the field it concerns (empty for the claim as a whole).</summary>
public record Problem(string Field, string Message);

/// <summary>Owns a claim from its first draft onwards. Status and visibility rules live here and on <see cref="Claim"/>.</summary>
public class ClaimService(KlaiminDb db)
{
    public Task<List<Claim>> MineAsync(string claimantId) =>
        db.Claims.Include(claim => claim.Receipts)
            .Where(claim => claim.ClaimantId == claimantId)
            .OrderByDescending(claim => claim.Id)
            .ToListAsync();

    public Task<List<Category>> ActiveCategoriesAsync() =>
        db.Categories.Where(category => category.IsActive).OrderBy(category => category.Id).ToListAsync();

    public async Task<Claim> StartAsync(string claimantId, string title)
    {
        var claim = new Claim { ClaimantId = claimantId, Title = title.Trim() };
        db.Claims.Add(claim);
        await db.SaveChangesAsync();
        return claim;
    }

    /// <summary>The claim with its receipts, or null when it does not exist or the viewer may not see it.</summary>
    public async Task<Claim?> FindAsync(int id, Viewer viewer)
    {
        var found = await db.Claims
            .Include(claim => claim.Claimant)
            .Include(claim => claim.Receipts).ThenInclude(receipt => receipt.Category)
            .Include(claim => claim.Receipts).ThenInclude(receipt => receipt.LineItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(claim => claim.Id == id);
        return found?.CanBeSeenBy(viewer) == true ? found : null;
    }

    public async Task<Receipt?> FindReceiptAsync(int receiptId, Viewer viewer)
    {
        var claimId = await db.Receipts
            .Where(receipt => receipt.Id == receiptId)
            .Select(receipt => (int?)receipt.ClaimId)
            .FirstOrDefaultAsync();
        var claim = claimId is null ? null : await FindAsync(claimId.Value, viewer);
        return claim?.Receipts.Single(receipt => receipt.Id == receiptId);
    }

    /// <summary>Turns a stored image and the fields the claimant confirmed into a receipt on the claim.</summary>
    public async Task<Problem?> AddReceiptAsync(Claim claim, string actorId, ReceiptEntry entry, StoredImage image)
    {
        RequireEditable(claim, actorId);

        if (entry.Total <= 0) return new("Total", "The receipt total must be more than Rp 0.");
        if (entry.Date > DateOnly.FromDateTime(DateTime.Now))
            return new("Date", "The receipt date cannot be in the future.");
        if (!await db.Categories.AnyAsync(category => category.Id == entry.CategoryId && category.IsActive))
            return new("CategoryId", "Choose a category from the list.");
        if (await db.Receipts.AnyAsync(receipt => receipt.ImageFile == image.File))
            return new("", "This photo is already saved as a receipt. Upload the next receipt instead.");

        claim.Receipts.Add(new Receipt
        {
            ImageFile = image.File,
            ImageContentType = image.ContentType,
            Total = entry.Total,
            Date = entry.Date,
            CategoryId = entry.CategoryId,
            LineItems = [.. entry.LineItems],
        });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<Problem?> SubmitAsync(Claim claim, string actorId)
    {
        RequireEditable(claim, actorId);

        if (claim.Receipts.Count == 0) return new("", "Add at least one receipt before submitting.");

        claim.Status = ClaimStatus.AwaitingManager;
        claim.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    private static void RequireEditable(Claim claim, string actorId)
    {
        if (!claim.CanBeEditedBy(actorId))
            throw new InvalidOperationException($"Claim {claim.Id} cannot be changed by this person in status {claim.Status}.");
    }
}
