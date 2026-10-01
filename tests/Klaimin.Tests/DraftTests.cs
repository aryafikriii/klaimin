using System.Net;
using System.Text.RegularExpressions;

namespace Klaimin.Tests;

public class DraftTests
{
    private static string EditAddress(string claimPage) =>
        Regex.Match(claimPage, "href=\"(/Receipts/Edit/\\d+)\"").Groups[1].Value;

    private static string Remove(string edit) => edit.Replace("Edit", "Remove");

    private static string Delete(string claim) => claim.Replace("Details", "Delete");

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, string claim, string edit, string total = "130.000") =>
        client.PostFormAsync(claim, edit, new()
        {
            ["Total"] = total,
            ["Date"] = "2026-09-20",
            ["CategoryId"] = "2",
            ["LineItems[0].Name"] = "Parkir",
            ["LineItems[0].Price"] = "5.000",
        });

    [Fact]
    public async Task A_draft_takes_several_receipts_and_the_claim_total_is_their_sum()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        await client.AddReceiptAsync(claim, "125.000", "Meals");
        await client.AddReceiptAsync(claim, "20.000", "Transport");

        var page = await client.GetStringAsync(claim);
        Assert.Equal(2, Regex.Count(page, "<article class=\"receipt\">"));
        Assert.Matches("(?s)<dt>Claim total</dt>\\s*<dd class=\"amount\">Rp 145.000</dd>", page);
    }

    [Fact]
    public async Task The_edit_form_shows_the_receipt_as_it_was_confirmed()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var edit = EditAddress(await (await client.AddReceiptAsync(claim)).Content.ReadAsStringAsync());

        var form = await client.GetStringAsync(edit);

        Assert.Matches("<input[^>]*name=\"Total\"[^>]*value=\"125.000\"", form);
        Assert.Matches("<input[^>]*name=\"Date\"[^>]*value=\"2026-09-14\"", form);
        Assert.Matches("<option value=\"\\d+\" selected=\"selected\">Meals</option>", form);
        Assert.Contains("<img src=\"/Receipts/Image/1\"", form);
    }

    [Fact]
    public async Task Editing_a_receipt_replaces_its_fields_and_the_claim_total_follows()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        await client.AddReceiptAsync(claim, "20.000", "Other");
        var uploaded = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();
        var added = await client.ConfirmAsync(claim, uploaded, new()
        {
            ["Total"] = "125.000",
            ["Date"] = "2026-09-14",
            ["CategoryId"] = "1",
            ["LineItems[0].Name"] = "Nasi goreng",
            ["LineItems[0].Price"] = "45.000",
        });
        var edit = Regex.Matches(await added.Content.ReadAsStringAsync(), "href=\"(/Receipts/Edit/\\d+)\"")[1].Groups[1].Value;

        var response = await EditAsync(client, claim, edit);

        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Matches("(?s)<dt>Claim total</dt>\\s*<dd class=\"amount\">Rp 150.000</dd>", page);
        Assert.Contains("Rp 130.000", page);
        Assert.Contains("20 Sep 2026", page);
        Assert.Contains("Transport", page);
        Assert.Contains("Parkir", page);
        Assert.DoesNotContain("Rp 125.000", page);
        Assert.DoesNotContain("Meals", page);
        Assert.DoesNotContain("Nasi goreng", page);
    }

    [Fact]
    public async Task An_edit_that_fails_validation_changes_nothing_and_keeps_what_was_typed()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var edit = EditAddress(await (await client.AddReceiptAsync(claim)).Content.ReadAsStringAsync());

        var response = await EditAsync(client, claim, edit, total: "0");

        var form = await response.Content.ReadAsStringAsync();
        Assert.Contains("The receipt total must be more than Rp 0.", form);
        Assert.Matches("<input[^>]*name=\"LineItems\\[0\\].Name\"[^>]*value=\"Parkir\"", form);
        Assert.Contains("Rp 125.000", await client.GetStringAsync(claim));
    }

    [Fact]
    public async Task Removing_a_receipt_takes_it_off_the_claim_and_deletes_its_image()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        await client.AddReceiptAsync(claim, "20.000", "Transport");
        var edit = EditAddress(await (await client.AddReceiptAsync(claim, "125.000", "Meals")).Content.ReadAsStringAsync());

        var response = await client.PostFormAsync(claim, Remove(edit));

        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Single(Regex.Matches(page, "<article class=\"receipt\">"));
        Assert.Matches("(?s)<dt>Claim total</dt>\\s*<dd class=\"amount\">Rp 125.000</dd>", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Receipts/Image/1")).StatusCode);
        Assert.Equal(1, app.StoredImageCount);
    }

    [Fact]
    public async Task A_draft_can_be_deleted_with_its_receipts_and_images()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        await client.AddReceiptAsync(claim);
        await client.AddReceiptAsync(claim, "20.000", "Transport");

        var response = await client.PostFormAsync(claim, Delete(claim));

        Assert.Equal("/", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("You have no claims yet.", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(claim)).StatusCode);
        Assert.Equal(0, app.StoredImageCount);
    }

    [Fact]
    public async Task Once_submitted_a_claim_cannot_be_deleted_and_its_receipts_cannot_be_changed()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var edit = EditAddress(await (await client.AddReceiptAsync(claim)).Content.ReadAsStringAsync());

        var page = await (await client.SubmitAsync(claim)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("/Receipts/Edit/", page);
        Assert.DoesNotContain("Remove receipt", page);
        Assert.DoesNotContain("Delete claim", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EditAsync(client, claim, edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync(claim, Remove(edit))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync(claim, Delete(claim))).StatusCode);
        Assert.Contains("Rp 125.000", await client.GetStringAsync(claim));
        Assert.Equal(1, app.StoredImageCount);
    }

    [Fact]
    public async Task A_returned_claim_can_be_revised_but_not_deleted()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var edit = EditAddress(await (await client.AddReceiptAsync(claim)).Content.ReadAsStringAsync());
        await client.AddReceiptAsync(claim, "20.000", "Transport");
        await client.SubmitAsync(claim);
        await (await app.SignedInAsync("manager")).DecideAsync(claim, "Return", "The dinner receipt is unreadable.");

        var deleted = await client.PostFormAsync(claim, Delete(claim));
        await client.PostFormAsync(claim, Remove(edit));
        await client.AddReceiptAsync(claim, "130.000", "Meals");

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        var page = await client.GetStringAsync(claim);
        Assert.DoesNotContain("Delete claim", page);
        Assert.Matches("(?s)<dt>Claim total</dt>\\s*<dd class=\"amount\">Rp 150.000</dd>", page);
    }

    [Fact]
    public async Task Nobody_else_can_change_or_delete_a_claim()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        var edit = EditAddress(await (await claimant.AddReceiptAsync(claim)).Content.ReadAsStringAsync());
        var manager = await app.SignedInAsync("manager");

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EditAsync(manager, "/", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostFormAsync("/", Remove(edit))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostFormAsync("/", Delete(claim))).StatusCode);
        Assert.Contains("Rp 125.000", await claimant.GetStringAsync(claim));
    }
}
