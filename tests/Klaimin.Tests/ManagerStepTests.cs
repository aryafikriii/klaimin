using System.Net;

namespace Klaimin.Tests;

public class ManagerStepTests
{
    /// <summary>The claimant's claim with one receipt of Rp 125.000, submitted to the manager step.</summary>
    private static async Task<(HttpClient Claimant, string Claim)> SubmittedClaimAsync(KlaiminApp app)
    {
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim);
        await claimant.SubmitAsync(claim);
        return (claimant, claim);
    }

    [Fact]
    public async Task Manager_sees_the_awaiting_claims_of_direct_reports_only()
    {
        using var app = new KlaiminApp();
        var (claimant, _) = await SubmittedClaimAsync(app);
        await claimant.StartClaimAsync("Still a draft");
        var manager = await app.SignedInAsync("manager");
        var own = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(own);
        await manager.SubmitAsync(own);

        var approvals = await manager.GetStringAsync("/Approvals");

        Assert.Contains("Client visit to Bandung", approvals);
        Assert.Contains("claimant@klaimin.test", approvals);
        Assert.Contains("Rp 125.000", approvals);
        Assert.DoesNotContain("Still a draft", approvals);
        Assert.DoesNotContain("Taxi to the airport", approvals);
    }

    [Fact]
    public async Task Someone_with_no_reports_has_nothing_to_decide()
    {
        using var app = new KlaiminApp();
        await SubmittedClaimAsync(app);
        var finance = await app.SignedInAsync("finance");

        var approvals = await finance.GetStringAsync("/Approvals");

        Assert.Contains("No claims are waiting for your decision.", approvals);
        Assert.DoesNotContain("Client visit to Bandung", approvals);
    }

    [Fact]
    public async Task Manager_opens_the_claim_and_sees_each_receipt_with_its_image_and_fields()
    {
        using var app = new KlaiminApp();
        var (_, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");

        var page = await manager.GetStringAsync(claim);

        Assert.Contains("claimant@klaimin.test", page);
        Assert.Contains("src=\"/Receipts/Image/1\"", page);
        Assert.Contains("14 Sep 2026", page);
        Assert.Contains("Meals", page);
        Assert.Contains("Rp 125.000", page);
    }

    [Fact]
    public async Task Approve_needs_no_comment_and_the_claimant_sees_the_decision()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");

        var response = await manager.DecideAsync(claim, "Approve");

        Assert.Equal("/Approvals", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("Client visit to Bandung", await response.Content.ReadAsStringAsync());
        var page = await claimant.GetStringAsync(claim);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Approved</dd>", page);
        Assert.Matches("(?s)Approved by manager@klaimin.test.*Manager step", page);
    }

    [Theory]
    [InlineData("Return")]
    [InlineData("Reject")]
    public async Task Return_and_reject_need_a_comment(string kind)
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");

        var response = await manager.DecideAsync(claim, kind, comment: "  ");

        Assert.Contains("Write a comment so the claimant knows why.", await response.Content.ReadAsStringAsync());
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting manager</dd>", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task Returned_claim_can_be_revised_and_resubmitted_and_keeps_every_decision()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");

        await manager.DecideAsync(claim, "Return", "The parking receipt is missing.");
        var returned = await claimant.GetStringAsync(claim);
        await claimant.AddReceiptAsync(claim, total: "20.000", category: "Transport");
        var resubmitted = await (await claimant.SubmitAsync(claim)).Content.ReadAsStringAsync();
        await manager.DecideAsync(claim, "Approve", "Thanks for adding it.");
        var approved = await claimant.GetStringAsync(claim);

        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Returned</dd>", returned);
        Assert.Contains("The parking receipt is missing.", returned);
        Assert.Contains("Add receipt", returned);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting manager</dd>", resubmitted);
        Assert.Contains("Rp 145.000", resubmitted);
        Assert.Matches(
            "(?s)Returned by manager@klaimin.test.*The parking receipt is missing\\..*Approved by manager@klaimin.test.*Thanks for adding it\\.",
            approved);
    }

    [Fact]
    public async Task Rejected_claim_is_final()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");

        await manager.DecideAsync(claim, "Reject", "This was a personal dinner.");

        var page = await claimant.GetStringAsync(claim);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Rejected</dd>", page);
        Assert.Contains("This was a personal dinner.", page);
        Assert.DoesNotContain("Add receipt", page);
        Assert.Equal(HttpStatusCode.NotFound, (await claimant.GetAsync(HttpClientExtensions.UploadForm(claim))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await claimant.SubmitAsync(claim)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DecideAsync(claim, "Approve")).StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_decide_their_own_claim()
    {
        using var app = new KlaiminApp();
        var manager = await app.SignedInAsync("manager");
        var own = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(own);
        await manager.SubmitAsync(own);

        var response = await manager.DecideAsync(own, "Approve");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var page = await manager.GetStringAsync(own);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting finance</dd>", page);
        Assert.DoesNotContain("name=\"kind\"", page);
    }

    [Theory]
    [InlineData("claimant")]
    [InlineData("finance")]
    [InlineData("admin")]
    public async Task Only_the_claimants_manager_can_decide_at_the_manager_step(string role)
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var other = role == "claimant" ? claimant : await app.SignedInAsync(role);

        var response = await other.DecideAsync(claim, "Approve");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting manager</dd>", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task A_claim_from_outside_someones_reports_is_not_found()
    {
        using var app = new KlaiminApp();
        var manager = await app.SignedInAsync("manager");
        var claim = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(claim);
        await manager.SubmitAsync(claim);
        var outsider = await app.SignedInAsync("claimant");

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(claim)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DecideAsync(claim, "Approve")).StatusCode);
    }
}
