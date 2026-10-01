using System.Net;
using System.Text.RegularExpressions;

namespace Klaimin.Tests;

public class PolicyFlagTests
{
    private const string MealsFlag = "The total is above the Meals cap of Rp 150.000.";

    private static string JustifyAddress(string claimPage) =>
        Regex.Match(claimPage, "action=\"(/Receipts/Justify/\\d+)\"").Groups[1].Value;

    private static Task<HttpResponseMessage> JustifyAsync(HttpClient client, string claim, string address, string text) =>
        client.PostFormAsync(claim, address, new() { ["justification"] = text });

    /// <summary>Edits receipt 1 into a meals receipt of the given total.</summary>
    private static Task<HttpResponseMessage> EditMealAsync(HttpClient client, string claim, string total) =>
        client.PostFormAsync(claim, "/Receipts/Edit/1", new() { ["Total"] = total, ["Date"] = "2026-09-14", ["CategoryId"] = "1" });

    [Theory]
    [InlineData("150.001", true)]
    [InlineData("150.000", false)]
    [InlineData("149.999", false)]
    public async Task A_receipt_is_flagged_only_when_its_total_is_above_its_categorys_cap(string total, bool flagged)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        var page = await (await client.AddReceiptAsync(claim, total, "Meals")).Content.ReadAsStringAsync();

        Assert.Equal(flagged, page.Contains("Policy flag."));
        Assert.Equal(flagged, page.Contains(MealsFlag));
    }

    [Fact]
    public async Task A_flagged_receipt_without_a_justification_stops_submission_and_is_named()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        await client.AddReceiptAsync(claim, "90.000", "Meals");
        await client.AddReceiptAsync(claim, "200.000", "Meals");

        var page = await (await client.SubmitAsync(claim)).Content.ReadAsStringAsync();

        Assert.Contains(
            "Write a justification for the receipt dated 14 Sep 2026 (Rp 200.000) before submitting. It is above the Meals cap.",
            page);
        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Draft</dd>", page);
    }

    [Fact]
    public async Task A_justification_cannot_be_blank()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var address = JustifyAddress(await (await client.AddReceiptAsync(claim, "200.000", "Meals")).Content.ReadAsStringAsync());

        var page = await (await JustifyAsync(client, claim, address, "   ")).Content.ReadAsStringAsync();

        Assert.Contains("Write why the receipt dated 14 Sep 2026 is above the cap.", page);
        Assert.Contains("Write a justification for the receipt", await (await client.SubmitAsync(claim)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_a_justification_the_claim_is_submitted_and_the_manager_sees_flag_and_reason()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var address = JustifyAddress(await (await client.AddReceiptAsync(claim, "200.000", "Meals")).Content.ReadAsStringAsync());

        await JustifyAsync(client, claim, address, "Dinner for four with the client team.");
        var submitted = await (await client.SubmitAsync(claim)).Content.ReadAsStringAsync();

        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting manager</dd>", submitted);
        var managerView = await (await app.SignedInAsync("manager")).GetStringAsync(claim);
        Assert.Contains(MealsFlag, managerView);
        Assert.Contains("Dinner for four with the client team.", managerView);
        Assert.DoesNotContain("Save justification", managerView);
    }

    [Fact]
    public async Task Finance_sees_the_flag_and_the_justification_too()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var address = JustifyAddress(await (await client.AddReceiptAsync(claim, "1.200.000", "Lodging")).Content.ReadAsStringAsync());
        await JustifyAsync(client, claim, address, "Only hotel near the venue during the conference.");
        await client.SubmitAsync(claim);
        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Approve");

        var financeView = await (await app.SignedInAsync("finance")).GetStringAsync(claim);

        Assert.Matches("(?s)<dt>Status</dt>\\s*<dd>Awaiting finance</dd>", financeView);
        Assert.Contains("The total is above the Lodging cap of Rp 1.000.000.", financeView);
        Assert.Contains("Only hotel near the venue during the conference.", financeView);
    }

    [Fact]
    public async Task Editing_a_receipt_below_the_cap_clears_the_flag_and_its_justification()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var address = JustifyAddress(await (await client.AddReceiptAsync(claim, "200.000", "Meals")).Content.ReadAsStringAsync());
        await JustifyAsync(client, claim, address, "Dinner for four with the client team.");

        var lowered = await (await EditMealAsync(client, claim, "120.000")).Content.ReadAsStringAsync();
        var raised = await (await EditMealAsync(client, claim, "180.000")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Policy flag.", lowered);
        Assert.DoesNotContain("Dinner for four", lowered);
        Assert.Contains(MealsFlag, raised);
        Assert.DoesNotContain("Dinner for four", raised);
    }

    [Fact]
    public async Task Editing_a_flagged_receipt_that_stays_flagged_keeps_its_justification()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var address = JustifyAddress(await (await client.AddReceiptAsync(claim, "200.000", "Meals")).Content.ReadAsStringAsync());
        await JustifyAsync(client, claim, address, "Dinner for four with the client team.");

        var page = await (await EditMealAsync(client, claim, "210.000")).Content.ReadAsStringAsync();

        Assert.Contains(MealsFlag, page);
        Assert.Contains("Dinner for four with the client team.", page);
    }

    [Fact]
    public async Task Only_the_claimant_can_justify_and_only_while_the_claim_is_editable()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        var address = JustifyAddress(await (await claimant.AddReceiptAsync(claim, "200.000", "Meals")).Content.ReadAsStringAsync());
        await claimant.AddReceiptAsync(claim, "90.000", "Meals");
        var manager = await app.SignedInAsync("manager");

        var byManager = await JustifyAsync(manager, "/", address, "Looks fine to me.");
        var unflagged = await JustifyAsync(claimant, claim, "/Receipts/Justify/2", "Nothing to justify.");
        await JustifyAsync(claimant, claim, address, "Dinner for four with the client team.");
        await claimant.SubmitAsync(claim);
        var afterSubmit = await JustifyAsync(claimant, claim, address, "Changed my mind.");

        Assert.Equal(HttpStatusCode.NotFound, byManager.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unflagged.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterSubmit.StatusCode);
        Assert.Contains("Dinner for four with the client team.", await manager.GetStringAsync(claim));
    }
}
