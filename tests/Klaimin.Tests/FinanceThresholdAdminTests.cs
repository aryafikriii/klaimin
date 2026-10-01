using System.Net;

namespace Klaimin.Tests;

public class FinanceThresholdAdminTests
{
    private static void AssertStatus(string status, string page) =>
        Assert.Matches($"(?s)<dt>Status</dt>\\s*<dd>{status}</dd>", page);

    private static Task<HttpResponseMessage> SetThresholdAsync(HttpClient admin, string threshold) =>
        admin.PostFormAsync("/Settings", "/Settings", new() { ["FinanceThreshold"] = threshold });

    /// <summary>The claimant's claim with one lodging receipt of Rp 600.000, submitted to the manager step.</summary>
    private static async Task<(HttpClient Claimant, string Claim)> SubmittedClaimAsync(KlaiminApp app)
    {
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "600.000", "Lodging");
        await claimant.SubmitAsync(claim);
        return (claimant, claim);
    }

    [Theory]
    [InlineData("claimant")]
    [InlineData("manager")]
    [InlineData("finance")]
    public async Task Other_roles_are_forbidden_from_the_settings_page(string role)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync(role);

        var page = await client.GetAsync("/Settings");
        var change = await client.PostFormAsync("/", "/Settings", new() { ["FinanceThreshold"] = "1" });

        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.DoesNotContain("href=\"/Settings\"", await client.GetStringAsync("/"));
        var settings = await (await app.SignedInAsync("admin")).GetStringAsync("/Settings");
        Assert.Matches("<input[^>]*name=\"FinanceThreshold\"[^>]*value=\"1.000.000\"", settings);
    }

    [Fact]
    public async Task Admin_sees_the_seeded_threshold_and_changes_it()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var before = await admin.GetStringAsync("/Settings");
        var response = await SetThresholdAsync(admin, "2500000");

        Assert.Matches("<input[^>]*name=\"FinanceThreshold\"[^>]*value=\"1.000.000\"", before);
        Assert.Contains("href=\"/Settings\"", before);
        var after = await response.Content.ReadAsStringAsync();
        Assert.Equal("/Settings", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("The finance threshold is now Rp 2.500.000.", after);
        Assert.Matches("<input[^>]*name=\"FinanceThreshold\"[^>]*value=\"2.500.000\"", after);
    }

    [Theory]
    [InlineData("", "Enter the threshold in whole Rupiah, for example 1.000.000.")]
    [InlineData("1,5 juta", "Enter the threshold in whole Rupiah, for example 1.000.000.")]
    [InlineData("-1", "Enter the threshold in whole Rupiah, for example 1.000.000.")]
    [InlineData("0", "The threshold must be more than Rp 0.")]
    public async Task The_threshold_must_be_a_positive_whole_number_of_Rupiah(string threshold, string message)
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var response = await SetThresholdAsync(admin, threshold);

        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.Matches(
            "<input[^>]*name=\"FinanceThreshold\"[^>]*value=\"1.000.000\"", await admin.GetStringAsync("/Settings"));
    }

    [Fact]
    public async Task A_claim_submitted_after_the_change_is_routed_by_the_new_threshold()
    {
        using var app = new KlaiminApp();
        await SetThresholdAsync(await app.SignedInAsync("admin"), "500.000");
        var (claimant, claim) = await SubmittedClaimAsync(app);

        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve");

        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task A_claim_submitted_before_a_lower_threshold_keeps_its_route_past_finance()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        await SetThresholdAsync(await app.SignedInAsync("admin"), "500.000");

        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve");

        AssertStatus("Approved", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task A_claim_submitted_before_a_higher_threshold_still_goes_to_finance()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");
        await SetThresholdAsync(admin, "500.000");
        var (claimant, claim) = await SubmittedClaimAsync(app);
        await SetThresholdAsync(admin, "2.000.000");

        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve");

        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
    }

    [Fact]
    public async Task A_returned_claim_takes_the_threshold_in_force_when_it_is_resubmitted()
    {
        using var app = new KlaiminApp();
        var (claimant, claim) = await SubmittedClaimAsync(app);
        var manager = await app.SignedInAsync("manager");
        await manager.DecideAsync(claim, "Return", "Add the booking confirmation.");
        await SetThresholdAsync(await app.SignedInAsync("admin"), "500.000");

        await claimant.SubmitAsync(claim);
        await manager.DecideAsync(claim, "Approve");

        AssertStatus("Awaiting finance", await claimant.GetStringAsync(claim));
    }
}
