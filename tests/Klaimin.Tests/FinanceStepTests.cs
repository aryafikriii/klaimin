using System.Net;

namespace Klaimin.Tests;

public class FinanceStepTests
{
    private const string Status = "(?s)<dt>Status</dt>\\s*<dd>{0}</dd>";

    private static void AssertStatus(string status, string page) => Assert.Matches(string.Format(Status, status), page);

    /// <summary>The claimant's claim with one lodging receipt of the given total, submitted.</summary>
    private static async Task<(HttpClient Claimant, string Claim)> SubmittedClaimAsync(KlaiminApp app, string total)
    {
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, total, "Lodging");
        await claimant.SubmitAsync(claim);
        return (claimant, claim);
    }

    /// <summary>A claim of Rp 1.000.001, approved by the manager and so waiting at the finance step.</summary>
    private static async Task<(HttpClient Claimant, string Claim)> ClaimAtFinanceAsync(KlaiminApp app)
    {
        var (claimant, claim) = await SubmittedClaimAsync(app, "1.000.001");
        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve", "Hotel rate checked.");
        return (claimant, claim);
    }

    [Fact]
    public async Task A_claim_at_the_threshold_is_approved_for_good_by_the_manager()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app, "1.000.000");

        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve");

        AssertStatus("Approved", await claimant.GetStringAsync(claim));
        Assert.DoesNotContain("Client visit to Bandung", await (await app.SignedInAsync("finance")).GetStringAsync("/Approvals"));
    }

    [Fact]
    public async Task A_claim_above_the_threshold_goes_to_finance_after_the_manager_approves()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app, "1.000.001");
        var manager = await app.SignedInAsync("manager");
        var finance = await app.SignedInAsync("finance");

        var before = await finance.GetStringAsync("/Approvals");
        await manager.DecideAsync(claim, "Approve", "Hotel rate checked.");

        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
        Assert.DoesNotContain("Client visit to Bandung", before);
        Assert.DoesNotContain("Client visit to Bandung", await manager.GetStringAsync("/Approvals"));
        var approvals = await finance.GetStringAsync("/Approvals");
        Assert.Contains("Client visit to Bandung", approvals);
        Assert.Contains("Rp 1.000.001", approvals);
        Assert.Contains("Approved by manager@klaimin.test: Hotel rate checked.", approvals);
    }

    [Fact]
    public async Task Finance_approval_completes_the_claim_and_both_decisions_are_kept()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await ClaimAtFinanceAsync(app);

        await (await app.SignedInAsync("finance")).DecideAsync(claim, "Approve");

        var page = await claimant.GetStringAsync(claim);
        AssertStatus("Approved", page);
        Assert.Matches(
            "(?s)Approved by manager@klaimin.test.*Manager step.*Hotel rate checked\\..*Approved by finance@klaimin.test.*Finance step",
            page);
    }

    [Theory]
    [InlineData("Return")]
    [InlineData("Reject")]
    public async Task Finance_needs_a_comment_to_return_or_reject(string kind)
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await ClaimAtFinanceAsync(app);

        var response = await (await app.SignedInAsync("finance")).DecideAsync(claim, kind);

        Assert.Contains("Write a comment so the claimant knows why.", await response.Content.ReadAsStringAsync());
        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task Finance_can_reject_for_good()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await ClaimAtFinanceAsync(app);

        await (await app.SignedInAsync("finance")).DecideAsync(claim, "Reject", "Above the travel policy rate.");

        var page = await claimant.GetStringAsync(claim);
        AssertStatus("Rejected", page);
        Assert.Matches("(?s)Rejected by finance@klaimin.test.*Finance step.*Above the travel policy rate\\.", page);
        Assert.Equal(HttpStatusCode.NotFound, (await claimant.SubmitAsync(claim)).StatusCode);
    }

    [Fact]
    public async Task A_claim_returned_by_finance_starts_again_at_the_manager_step()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await ClaimAtFinanceAsync(app);
        await (await app.SignedInAsync("finance")).DecideAsync(claim, "Return", "Attach the booking confirmation.");

        var returned = await claimant.GetStringAsync(claim);
        var resubmitted = await (await claimant.SubmitAsync(claim)).Content.ReadAsStringAsync();

        AssertStatus("Returned", returned);
        AssertStatus("Awaiting manager", resubmitted);
        Assert.Contains("Client visit to Bandung", await (await app.SignedInAsync("manager")).GetStringAsync("/Approvals"));
    }

    [Fact]
    public async Task A_claimant_with_no_manager_starts_at_finance_whatever_the_total()
    {
        using var app = new KlaiminApp();
        var manager = await app.SignedInAsync("manager");
        var claim = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(claim, "85.000", "Transport");

        var submitted = await (await manager.SubmitAsync(claim)).Content.ReadAsStringAsync();
        await (await app.SignedInAsync("finance")).DecideAsync(claim, "Approve");

        AssertStatus("Awaiting finance", submitted);
        var page = await manager.GetStringAsync(claim);
        AssertStatus("Approved", page);
        Assert.Matches("(?s)Approved by finance@klaimin.test.*Finance step", page);
    }

    [Fact]
    public async Task Finance_cannot_decide_their_own_claim()
    {
        using var app = new KlaiminApp();
        var finance = await app.SignedInAsync("finance");
        var own = await finance.StartClaimAsync("Audit travel");
        await finance.AddReceiptAsync(own);
        await finance.SubmitAsync(own);

        var response = await finance.DecideAsync(own, "Approve");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var page = await finance.GetStringAsync(own);
        AssertStatus("Awaiting finance", page);
        Assert.DoesNotContain("name=\"kind\"", page);
        Assert.DoesNotContain("Audit travel", await finance.GetStringAsync("/Approvals"));
    }

    [Theory]
    [InlineData("claimant")]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task Only_finance_can_decide_at_the_finance_step(string role)
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await ClaimAtFinanceAsync(app);
        var other = role == "claimant" ? claimant : await app.SignedInAsync(role);

        var response = await other.DecideAsync(claim, "Approve");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
    }
}
