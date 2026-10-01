namespace Klaimin.Core;

public enum ClaimStatus
{
    Draft,
    AwaitingManager,
    Approved,
    Returned,
    Rejected,
}

public enum ApprovalStep
{
    Manager,
}

public enum DecisionKind
{
    Approve,
    Return,
    Reject,
}

public class Claim
{
    public int Id { get; set; }
    public string ClaimantId { get; set; } = "";
    public AppUser Claimant { get; set; } = null!;
    public string Title { get; set; } = "";
    public ClaimStatus Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<Receipt> Receipts { get; set; } = [];
    public List<Decision> Decisions { get; set; } = [];

    public long Total => Receipts.Sum(receipt => receipt.Total);

    public string StatusLabel => Status switch
    {
        ClaimStatus.Draft => "Draft",
        ClaimStatus.AwaitingManager => "Awaiting manager",
        ClaimStatus.Approved => "Approved",
        ClaimStatus.Returned => "Returned",
        ClaimStatus.Rejected => "Rejected",
        _ => throw new InvalidOperationException($"No label for status {Status}."),
    };

    /// <summary>A returned claim is back in the claimant's hands, like a draft.</summary>
    public bool CanBeEditedBy(string userId) =>
        ClaimantId == userId && Status is ClaimStatus.Draft or ClaimStatus.Returned;

    /// <summary>Only the claimant's manager decides at the manager step, and nobody decides their own claim.</summary>
    public bool CanBeDecidedBy(string userId) =>
        Status == ClaimStatus.AwaitingManager && ClaimantId != userId && Claimant.ManagerId == userId;

    /// <summary>A draft is the claimant's alone. Once submitted, their manager, finance, and admins can see it too.</summary>
    public bool CanBeSeenBy(Viewer viewer) =>
        ClaimantId == viewer.UserId
        || Status != ClaimStatus.Draft && (viewer.IsFinance || viewer.IsAdmin || Claimant.ManagerId == viewer.UserId);
}

public class Decision
{
    public int Id { get; set; }
    public string ApproverId { get; set; } = "";
    public AppUser Approver { get; set; } = null!;
    public ApprovalStep Step { get; set; }
    public DecisionKind Kind { get; set; }
    public string? Comment { get; set; }
    public DateTime At { get; set; }

    public string KindLabel => Kind switch
    {
        DecisionKind.Approve => "Approved",
        DecisionKind.Return => "Returned",
        DecisionKind.Reject => "Rejected",
        _ => throw new InvalidOperationException($"No label for decision {Kind}."),
    };
}

public class Receipt
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public string ImageFile { get; set; } = "";
    public string ImageContentType { get; set; } = "";
    public long Total { get; set; }
    public DateOnly Date { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public List<LineItem> LineItems { get; set; } = [];
}

public class LineItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Price { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Cap { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>The person looking at a claim, reduced to what the visibility rule needs.</summary>
public record Viewer(string UserId, bool IsFinance, bool IsAdmin);
