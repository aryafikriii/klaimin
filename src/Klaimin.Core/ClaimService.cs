using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

/// <summary>What the claimant confirmed for one receipt.</summary>
public record ReceiptEntry(long Total, DateOnly Date, int CategoryId, IReadOnlyList<LineItem> LineItems);

/// <summary>Why a change to a claim was refused, and the field it concerns (empty for the claim as a whole).</summary>
public record Problem(string Field, string Message);

/// <summary>Owns a claim from its first draft onwards. Status and visibility rules live here and on <see cref="Claim"/>.</summary>
public class ClaimService(KlaiminDb db, ReceiptImages images)
{
    public Task<List<Claim>> MineAsync(string claimantId) =>
        db.Claims.Include(claim => claim.Receipts)
            .Where(claim => claim.ClaimantId == claimantId)
            .OrderByDescending(claim => claim.Id)
            .ToListAsync();

    /// <summary>Claims waiting for this person's decision, oldest first.</summary>
    public Task<List<Claim>> AwaitingAsync(Viewer approver) =>
        db.Claims.Include(claim => claim.Receipts).Include(claim => claim.Claimant)
            .Include(claim => claim.Decisions).ThenInclude(decision => decision.Approver)
            .Where(claim => claim.ClaimantId != approver.UserId
                && (claim.Status == ClaimStatus.AwaitingManager && claim.Claimant.ManagerId == approver.UserId
                    || claim.Status == ClaimStatus.AwaitingFinance && approver.IsFinance))
            .OrderBy(claim => claim.SubmittedAt)
            .AsSplitQuery()
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
            .Include(claim => claim.Receipts).ThenInclude(receipt => receipt.DuplicateOf!.Claim.Claimant)
            .Include(claim => claim.Decisions).ThenInclude(decision => decision.Approver)
            .AsSplitQuery()
            .FirstOrDefaultAsync(claim => claim.Id == id);
        return found?.CanBeSeenBy(viewer) == true ? found : null;
    }

    /// <summary>A receipt together with its claim, or null when the viewer may not see the claim.</summary>
    public async Task<(Claim Claim, Receipt Receipt)?> FindReceiptAsync(int receiptId, Viewer viewer)
    {
        var claimId = await db.Receipts
            .Where(receipt => receipt.Id == receiptId)
            .Select(receipt => (int?)receipt.ClaimId)
            .FirstOrDefaultAsync();
        var claim = claimId is null ? null : await FindAsync(claimId.Value, viewer);
        return claim is null ? null : (claim, claim.Receipts.Single(receipt => receipt.Id == receiptId));
    }

    /// <summary>Turns a stored image and the fields the claimant confirmed into a receipt on the claim.</summary>
    public async Task<Problem?> AddReceiptAsync(Claim claim, string actorId, ReceiptEntry entry, StoredImage image)
    {
        RequireEditable(claim, actorId);

        if (await CheckAsync(entry) is { } problem) return problem;
        if (await db.Receipts.AnyAsync(receipt => receipt.ImageFile == image.File))
            return new("", "This photo is already saved as a receipt. Upload the next receipt instead.");

        var receipt = new Receipt { ImageFile = image.File, ImageContentType = image.ContentType, ImageHash = image.Hash };
        receipt.Confirm(entry, await CapAsync(entry.CategoryId));
        receipt.DuplicateOfId = await FindDuplicateAsync(claim, receipt);
        claim.Receipts.Add(receipt);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<Problem?> UpdateReceiptAsync(Claim claim, string actorId, Receipt receipt, ReceiptEntry entry)
    {
        RequireEditable(claim, actorId);

        if (await CheckAsync(entry) is { } problem) return problem;

        receipt.Confirm(entry, await CapAsync(entry.CategoryId));
        receipt.DuplicateOfId = await FindDuplicateAsync(claim, receipt);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<Problem?> JustifyAsync(Claim claim, string actorId, Receipt receipt, string? justification)
    {
        RequireEditable(claim, actorId);
        if (receipt.ExceededCap is null)
            throw new InvalidOperationException($"Receipt {receipt.Id} has no policy flag to justify.");

        if (string.IsNullOrWhiteSpace(justification))
            return new("", $"Write why the receipt dated {receipt.DateLabel} is above the cap.");

        receipt.Justification = justification.Trim();
        await db.SaveChangesAsync();
        return null;
    }

    public async Task RemoveReceiptAsync(Claim claim, string actorId, Receipt receipt)
    {
        RequireEditable(claim, actorId);

        claim.Receipts.Remove(receipt);
        await db.SaveChangesAsync();
        images.Delete(receipt.ImageFile);
    }

    public async Task DeleteAsync(Claim claim, string actorId)
    {
        if (!claim.CanBeDeletedBy(actorId))
            throw new InvalidOperationException($"Claim {claim.Id} cannot be deleted by this person in status {claim.Status}.");

        var files = claim.Receipts.Select(receipt => receipt.ImageFile).ToList();
        db.Claims.Remove(claim);
        await db.SaveChangesAsync();
        // Files go last: a failed save must not leave receipts pointing at images that are gone.
        files.ForEach(images.Delete);
    }

    private async Task<Problem?> CheckAsync(ReceiptEntry entry)
    {
        if (entry.Total <= 0) return new("Total", "The receipt total must be more than Rp 0.");
        if (entry.Date > DateOnly.FromDateTime(DateTime.Now))
            return new("Date", "The receipt date cannot be in the future.");
        if (!await db.Categories.AnyAsync(category => category.Id == entry.CategoryId && category.IsActive))
            return new("CategoryId", "Choose a category from the list.");
        return null;
    }

    public async Task<Problem?> SubmitAsync(Claim claim, string actorId)
    {
        RequireEditable(claim, actorId);

        if (claim.Receipts.Count == 0) return new("", "Add at least one receipt before submitting.");
        if (claim.Receipts.FirstOrDefault(receipt => receipt.NeedsJustification) is { } flagged)
            return new("", $"Write a justification for the receipt dated {flagged.DateLabel} ({Rupiah.Format(flagged.Total)}) before submitting. "
                + $"It is above the {flagged.Category.Name} cap.");

        claim.FinanceThreshold = await FinanceThresholdAsync();
        // With no manager there is nobody for the manager step, so the claim starts at finance whatever its total.
        claim.Status = claim.Claimant.ManagerId is null ? ClaimStatus.AwaitingFinance : ClaimStatus.AwaitingManager;
        claim.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<Problem?> DecideAsync(Claim claim, Viewer approver, DecisionKind kind, string? comment)
    {
        if (!claim.CanBeDecidedBy(approver))
            throw new InvalidOperationException($"Claim {claim.Id} cannot be decided by this person in status {claim.Status}.");

        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (kind != DecisionKind.Approve && comment is null)
            return new("comment", "Write a comment so the claimant knows why.");

        var step = claim.Status == ClaimStatus.AwaitingFinance ? ApprovalStep.Finance : ApprovalStep.Manager;
        var needsFinance = step == ApprovalStep.Manager
            && claim.Total > (claim.FinanceThreshold ?? await FinanceThresholdAsync());
        claim.Decisions.Add(new Decision
        {
            ApproverId = approver.UserId,
            Step = step,
            Kind = kind,
            Comment = comment,
            At = DateTime.UtcNow,
        });
        claim.Status = kind switch
        {
            DecisionKind.Approve => needsFinance ? ClaimStatus.AwaitingFinance : ClaimStatus.Approved,
            DecisionKind.Return => ClaimStatus.Returned,
            DecisionKind.Reject => ClaimStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// The earliest other receipt this one looks like: the same photo on a claim that has been submitted (or on
    /// any claim of the same claimant), or the same total and date on another receipt of the same claimant.
    /// Another person's draft never counts, so a flag cannot reveal what someone has not submitted yet.
    /// </summary>
    private Task<int?> FindDuplicateAsync(Claim claim, Receipt receipt) =>
        db.Receipts
            .Where(other => other.Id != receipt.Id)
            .Where(other =>
                receipt.ImageHash != null && other.ImageHash == receipt.ImageHash
                    && (other.Claim.Status != ClaimStatus.Draft || other.Claim.ClaimantId == claim.ClaimantId)
                || other.Total == receipt.Total && other.Date == receipt.Date && other.Claim.ClaimantId == claim.ClaimantId)
            .OrderBy(other => other.Id)
            .Select(other => (int?)other.Id)
            .FirstOrDefaultAsync();

    private Task<long> CapAsync(int categoryId) =>
        db.Categories.Where(category => category.Id == categoryId).Select(category => category.Cap).SingleAsync();

    private Task<long> FinanceThresholdAsync() => db.Settings.Select(settings => settings.FinanceThreshold).SingleAsync();

    private static void RequireEditable(Claim claim, string actorId)
    {
        if (!claim.CanBeEditedBy(actorId))
            throw new InvalidOperationException($"Claim {claim.Id} cannot be changed by this person in status {claim.Status}.");
    }
}
