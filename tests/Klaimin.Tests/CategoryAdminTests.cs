using System.Net;
using System.Text.RegularExpressions;
using Klaimin.Core;

namespace Klaimin.Tests;

public class CategoryAdminTests
{
    private const string Meals = "/Categories/Edit/1";

    private static Task<HttpResponseMessage> SaveAsync(HttpClient admin, string address, string name, string cap) =>
        admin.PostFormAsync("/Categories", address, new() { ["Name"] = name, ["Cap"] = cap });

    private static Task<HttpResponseMessage> SetActiveAsync(HttpClient admin, int id, bool active) =>
        admin.PostFormAsync("/Categories", $"/Categories/SetActive/{id}", new() { ["active"] = active ? "true" : "false" });

    /// <summary>The categories offered to a claimant on the confirmation page of a fresh upload.</summary>
    private static async Task<string> OfferedAsync(HttpClient claimant, string claim)
    {
        var page = await (await claimant.UploadAsync(claim)).Content.ReadAsStringAsync();
        return Regex.Match(page, "(?s)<select.*?</select>").Value;
    }

    [Theory]
    [InlineData("claimant")]
    [InlineData("manager")]
    [InlineData("finance")]
    public async Task Other_roles_are_forbidden_from_the_category_pages(string role)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Categories/New")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Meals)).StatusCode);
        var add = await client.PostFormAsync("/", "/Categories/New", new() { ["Name"] = "Training", ["Cap"] = "750.000" });
        var change = await client.PostFormAsync("/", Meals, new() { ["Name"] = "Meals", ["Cap"] = "9.000.000" });
        var deactivate = await client.PostFormAsync("/", "/Categories/SetActive/1", new() { ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.DoesNotContain("href=\"/Categories\"", await client.GetStringAsync("/"));

        var categories = await (await app.SignedInAsync("admin")).GetStringAsync("/Categories");
        Assert.DoesNotContain("Training", categories);
        Assert.Matches("(?s)Meals.*?Rp 150\\.000.*?Yes", categories);
    }

    [Fact]
    public async Task Admin_sees_the_seeded_categories_with_their_caps()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var page = await admin.GetStringAsync("/Categories");

        Assert.Matches(
            "(?s)Meals.*Rp 150\\.000.*Transport.*Rp 300\\.000.*Lodging.*Rp 1\\.000\\.000.*Office supplies.*Rp 500\\.000.*Other.*Rp 250\\.000",
            page);
        Assert.Contains("href=\"/Categories\"", page);
    }

    [Fact]
    public async Task Admin_adds_a_category_and_claimants_are_offered_it()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();

        var response = await SaveAsync(admin, "/Categories/New", " Training ", "750.000");

        Assert.Equal("/Categories", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Matches("(?s)Training.*?Rp 750\\.000", await response.Content.ReadAsStringAsync());
        Assert.Contains(">Training</option>", await OfferedAsync(claimant, claim));
        var page = await (await claimant.AddReceiptAsync(claim, "750.001", "Training")).Content.ReadAsStringAsync();
        Assert.Contains("The total is above the Training cap of Rp 750.000.", page);
    }

    [Fact]
    public async Task Admin_edits_a_categorys_name_and_cap()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var form = await admin.GetStringAsync(Meals);
        var response = await SaveAsync(admin, Meals, "Meals and drinks", "175.000");

        Assert.Matches("<input[^>]*name=\"Name\"[^>]*value=\"Meals\"", form);
        Assert.Matches("<input[^>]*name=\"Cap\"[^>]*value=\"150.000\"", form);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Matches("(?s)Meals and drinks.*?Rp 175\\.000", page);
        Assert.DoesNotContain("Rp 150.000", page);
    }

    [Theory]
    [InlineData("  ", "150.000", "Give the category a name.")]
    [InlineData("meals", "150.000", "There is already a category called meals.")]
    [InlineData("Training", "0", "The cap must be more than Rp 0.")]
    [InlineData("Training", "", "Enter the cap in whole Rupiah, for example 150.000.")]
    [InlineData("Training", "1,5 juta", "Enter the cap in whole Rupiah, for example 150.000.")]
    [InlineData("Training", "-5", "Enter the cap in whole Rupiah, for example 150.000.")]
    public async Task A_category_needs_a_unique_name_and_a_positive_whole_cap(string name, string cap, string message)
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var response = await SaveAsync(admin, "/Categories/New", name, cap);

        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Training", await admin.GetStringAsync("/Categories"));
    }

    [Fact]
    public async Task Renaming_a_category_to_another_categorys_name_is_refused_but_keeping_its_own_is_fine()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");

        var clash = await SaveAsync(admin, Meals, "TRANSPORT", "150.000");
        var same = await SaveAsync(admin, Meals, "Meals", "160.000");

        Assert.Contains("There is already a category called TRANSPORT.", await clash.Content.ReadAsStringAsync());
        Assert.Matches("(?s)Meals.*?Rp 160\\.000", await same.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_deactivated_category_is_not_offered_or_suggested_but_stays_on_old_receipts()
    {
        using var app = new KlaiminApp
        {
            Extraction = new(ExtractionOutcome.Extracted, new Extraction(53_000, new DateOnly(2026, 9, 14), [], "Meals")),
        };
        var admin = await app.SignedInAsync("admin");
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "53.000", "Meals");

        var listed = await (await SetActiveAsync(admin, 1, active: false)).Content.ReadAsStringAsync();
        var offered = await OfferedAsync(claimant, claim);
        var refused = await claimant.PostFormAsync(claim, "/Receipts/Edit/1", new()
        {
            ["Total"] = "53.000", ["Date"] = "2026-09-14", ["CategoryId"] = "2",
        });
        var confirmPage = await (await claimant.UploadAsync(claim)).Content.ReadAsStringAsync();
        var sneaked = await claimant.ConfirmAsync(claim, confirmPage, new()
        {
            ["Total"] = "20.000", ["Date"] = "2026-09-15", ["CategoryId"] = "1",
        });

        Assert.Matches("(?s)Meals.*?No, deactivated.*?Reactivate", listed);
        Assert.DoesNotContain("Meals", offered);
        Assert.DoesNotContain("selected=\"selected\"", offered);
        Assert.Contains("Transport", await refused.Content.ReadAsStringAsync());
        Assert.Contains("Choose a category from the list.", await sneaked.Content.ReadAsStringAsync());

        var reoffered = await (await SetActiveAsync(admin, 1, active: true)).Content.ReadAsStringAsync();
        Assert.Matches("(?s)Meals.*?Yes.*?Deactivate", reoffered);
        Assert.Contains(">Meals</option>", await OfferedAsync(claimant, claim));
    }

    [Fact]
    public async Task A_receipt_keeps_its_deactivated_category_when_it_is_edited()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "53.000", "Meals");
        await SetActiveAsync(admin, 1, active: false);

        var form = await claimant.GetStringAsync("/Receipts/Edit/1");
        var response = await claimant.PostFormAsync(claim, "/Receipts/Edit/1", new()
        {
            ["Total"] = "60.000", ["Date"] = "2026-09-14", ["CategoryId"] = "1",
        });

        Assert.Matches("<option value=\"1\" selected=\"selected\">Meals</option>", form);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Rp 60.000", page);
        Assert.Contains("Meals", page);
    }

    [Fact]
    public async Task A_cap_change_leaves_receipts_already_confirmed_as_they_were()
    {
        using var app = new KlaiminApp();
        var admin = await app.SignedInAsync("admin");
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        await claimant.AddReceiptAsync(claim, "200.000", "Meals");
        await claimant.AddReceiptAsync(claim, "250.000", "Transport");

        await SaveAsync(admin, Meals, "Meals", "300.000");
        await SaveAsync(admin, "/Categories/Edit/2", "Transport", "100.000");
        var before = await claimant.GetStringAsync(claim);
        await claimant.AddReceiptAsync(claim, "210.000", "Meals");
        var after = await (await claimant.AddReceiptAsync(claim, "120.000", "Transport")).Content.ReadAsStringAsync();

        Assert.Single(Regex.Matches(before, "Policy flag\\."));
        Assert.Contains("The total is above the Meals cap of Rp 150.000.", before);
        Assert.Equal(2, Regex.Count(after, "Policy flag\\."));
        Assert.Contains("The total is above the Meals cap of Rp 150.000.", after);
        Assert.Contains("The total is above the Transport cap of Rp 100.000.", after);
    }

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_sign_in()
    {
        using var app = new KlaiminApp();

        var response = await app.CreateClient().GetAsync("/Categories");

        Assert.Equal("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
