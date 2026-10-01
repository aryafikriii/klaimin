using System.Net;
using System.Text.RegularExpressions;

namespace Klaimin.Tests;

public class DuplicateFlagTests
{
    private const string SamePhoto = "The photo is identical to the photo on another receipt.";
    private const string SameFields = "The total and date match another receipt from the same claimant.";
    private const string NoAccess = "The other receipt is on a claim you cannot open.";

    private static string OtherReceipt(string claimPage) =>
        Regex.Match(claimPage, "href=\"(/Claims/Details/\\d+#receipt-\\d+)\">Open the other receipt").Groups[1].Value;

    /// <summary>The manager's own claim holding one receipt with the given photo, submitted or left as a draft.</summary>
    private static async Task<string> ManagersClaimAsync(KlaiminApp app, byte[] photo, bool submit)
    {
        var manager = await app.SignedInAsync("manager");
        var claim = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(claim, "85.000", "Transport", photo, "2026-09-10");
        if (submit) await manager.SubmitAsync(claim);
        return claim;
    }

    [Fact]
    public async Task A_photo_identical_to_a_submitted_receipt_is_flagged_without_revealing_the_other_claim()
    {
        using var app = new KlaiminApp();
        var photo = HttpClientExtensions.NewPhoto();
        await ManagersClaimAsync(app, photo, submit: true);
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();

        var page = await (await claimant.AddReceiptAsync(claim, "40.000", "Transport", photo)).Content.ReadAsStringAsync();

        Assert.Contains("Duplicate flag.", page);
        Assert.Contains(SamePhoto, page);
        Assert.Contains(NoAccess, page);
        Assert.DoesNotContain("Open the other receipt", page);
        Assert.DoesNotContain("Taxi to the airport", page);
    }

    [Fact]
    public async Task A_photo_identical_to_someone_elses_draft_is_not_flagged()
    {
        using var app = new KlaiminApp();
        var photo = HttpClientExtensions.NewPhoto();
        await ManagersClaimAsync(app, photo, submit: false);
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();

        var page = await (await claimant.AddReceiptAsync(claim, "40.000", "Transport", photo)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Duplicate flag.", page);
    }

    [Fact]
    public async Task The_same_total_and_date_on_another_of_the_claimants_receipts_is_flagged_with_a_link()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var first = await claimant.StartClaimAsync("Client visit to Bandung");
        await claimant.AddReceiptAsync(first, "125.000", "Meals");
        var second = await claimant.StartClaimAsync("Team lunch");

        var page = await (await claimant.AddReceiptAsync(second, "125.000", "Other")).Content.ReadAsStringAsync();

        Assert.Contains(SameFields, page);
        Assert.Equal($"{first}#receipt-1", OtherReceipt(page));
        Assert.DoesNotContain("Duplicate flag.", await claimant.GetStringAsync(first));
    }

    [Theory]
    [InlineData("125.001", "2026-09-14")]
    [InlineData("125.000", "2026-09-13")]
    public async Task A_different_total_or_date_is_not_a_duplicate(string total, string date)
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");

        var page = await (await claimant.AddReceiptAsync(claim, total, "Meals", date: date)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Duplicate flag.", page);
    }

    [Fact]
    public async Task The_same_total_and_date_from_another_claimant_is_not_a_duplicate()
    {
        using var app = new KlaiminApp();
        var manager = await app.SignedInAsync("manager");
        var managersClaim = await manager.StartClaimAsync("Taxi to the airport");
        await manager.AddReceiptAsync(managersClaim, "125.000", "Meals");
        await manager.SubmitAsync(managersClaim);
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();

        var page = await (await claimant.AddReceiptAsync(claim, "125.000", "Meals")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Duplicate flag.", page);
    }

    [Fact]
    public async Task A_duplicate_flag_does_not_stop_submission_and_asks_for_no_justification()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");
        var flagged = await (await claimant.AddReceiptAsync(claim, "125.000", "Meals")).Content.ReadAsStringAsync();

        var submitted = await (await claimant.SubmitAsync(claim)).Content.ReadAsStringAsync();

        Assert.Contains("Duplicate flag.", flagged);
        Assert.DoesNotContain("name=\"justification\"", flagged);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting manager</dd>", submitted);
    }

    [Fact]
    public async Task Editing_a_receipt_recalculates_its_duplicate_flag()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");
        Task<HttpResponseMessage> EditSecondAsync(string total) => claimant.PostFormAsync(
            claim, "/Receipts/Edit/2", new() { ["Total"] = total, ["Date"] = "2026-09-14", ["CategoryId"] = "1" });

        var changed = await (await EditSecondAsync("110.000")).Content.ReadAsStringAsync();
        var changedBack = await (await EditSecondAsync("125.000")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Duplicate flag.", changed);
        Assert.Contains(SameFields, changedBack);
    }

    [Fact]
    public async Task Removing_the_earlier_receipt_clears_the_flag_that_pointed_at_it()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");
        await claimant.AddReceiptAsync(claim, "125.000", "Meals");

        var page = await (await claimant.PostFormAsync(claim, "/Receipts/Remove/1")).Content.ReadAsStringAsync();

        Assert.Single(Regex.Matches(page, "<article class=\"receipt\""));
        Assert.DoesNotContain("Duplicate flag.", page);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("finance")]
    public async Task An_approver_sees_the_flag_and_can_follow_it_to_the_other_receipt(string role)
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var earlier = await claimant.StartClaimAsync("Client visit to Bandung");
        await claimant.AddReceiptAsync(earlier, "125.000", "Meals");
        await claimant.SubmitAsync(earlier);
        var later = await claimant.StartClaimAsync("Team lunch");
        await claimant.AddReceiptAsync(later, "125.000", "Meals");
        await claimant.SubmitAsync(later);
        var approver = await app.SignedInAsync(role);

        var page = await approver.GetStringAsync(later);
        var followed = await approver.GetAsync(OtherReceipt(page));

        Assert.Contains(SameFields, page);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Contains("Client visit to Bandung", await followed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_approver_who_may_see_the_other_claim_gets_the_link_for_an_identical_photo()
    {
        using var app = new KlaiminApp();
        var photo = HttpClientExtensions.NewPhoto();
        var managersClaim = await ManagersClaimAsync(app, photo, submit: true);
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "40.000", "Transport", photo);
        await claimant.SubmitAsync(claim);

        var managerView = await (await app.SignedInAsync("manager")).GetStringAsync(claim);

        Assert.Contains(SamePhoto, managerView);
        Assert.Equal($"{managersClaim}#receipt-1", OtherReceipt(managerView));
    }
}
