using System.Net;
using System.Text.RegularExpressions;
using Klaimin.Core;

namespace Klaimin.Tests;

public class ExtractionTests
{
    private static ExtractionResult Extracted(string? category = "Meals") => new(
        ExtractionOutcome.Extracted,
        new Extraction(
            Total: 53_000,
            Date: new DateOnly(2026, 9, 14),
            LineItems: [new("Nasi goreng", 45_000), new("Es teh", 8_000)],
            Category: category));

    [Fact]
    public async Task Upload_shows_the_extraction_next_to_the_image_in_editable_fields()
    {
        using var app = new KlaiminApp { Extraction = Extracted() };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        var page = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();

        Assert.Contains("These fields were read from the photo.", page);
        Assert.Matches("<input[^>]*name=\"Total\"[^>]*value=\"53.000\"", page);
        Assert.Matches("<input[^>]*name=\"Date\"[^>]*value=\"2026-09-14\"", page);
        Assert.Matches("<option value=\"\\d+\" selected=\"selected\">Meals</option>", page);
        Assert.Matches("<input[^>]*name=\"LineItems\\[0\\].Name\"[^>]*value=\"Nasi goreng\"", page);
        Assert.Matches("<input[^>]*name=\"LineItems\\[0\\].Price\"[^>]*value=\"45.000\"", page);
        Assert.Matches("<input[^>]*name=\"LineItems\\[1\\].Name\"[^>]*value=\"Es teh\"", page);
        Assert.Matches("<input[^>]*name=\"LineItems\\[1\\].Price\"[^>]*value=\"8.000\"", page);
        Assert.DoesNotMatch("<(input|select)[^>]*(readonly|disabled)", page);

        var image = WebUtility.HtmlDecode(Regex.Match(page, "<img src=\"(/Receipts/Pending/[^\"]+)\"").Groups[1].Value);
        Assert.Equal(HttpClientExtensions.Png, await client.GetByteArrayAsync(image));
    }

    [Fact]
    public async Task Nothing_is_saved_until_the_claimant_confirms()
    {
        using var app = new KlaiminApp { Extraction = Extracted() };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        await client.UploadAsync(claim);

        var page = await client.GetStringAsync(claim);
        Assert.Contains("No receipts yet.", page);
        Assert.Contains("Rp 0", page);
    }

    [Fact]
    public async Task The_receipt_keeps_what_the_claimant_confirmed_not_what_was_extracted()
    {
        using var app = new KlaiminApp { Extraction = Extracted() };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var confirmPage = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();

        var response = await client.ConfirmAsync(claim, confirmPage, new()
        {
            ["Total"] = "58.000",
            ["Date"] = "2026-09-15",
            ["CategoryId"] = HttpClientExtensions.CategoryId(confirmPage, "Transport"),
            ["LineItems[0].Name"] = "Nasi goreng spesial",
            ["LineItems[0].Price"] = "50.000",
            ["LineItems[1].Name"] = "Es teh",
            ["LineItems[1].Price"] = "8.000",
        });

        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Rp 58.000", page);
        Assert.Contains("15 Sep 2026", page);
        Assert.Contains("Transport", page);
        Assert.Contains("Nasi goreng spesial", page);
        Assert.Contains("Rp 50.000", page);
        Assert.DoesNotContain("Rp 53.000", page);
        Assert.DoesNotContain("14 Sep 2026", page);
        Assert.DoesNotContain("Meals", page);
    }

    [Theory]
    [InlineData("meals", true)]
    [InlineData(" Meals ", true)]
    [InlineData("Entertainment", false)]
    [InlineData(null, false)]
    public async Task Suggested_category_is_an_active_category_or_left_empty(string? named, bool suggested)
    {
        using var app = new KlaiminApp { Extraction = Extracted(named) };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        var page = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();

        Assert.Equal(suggested, Regex.IsMatch(page, "<option value=\"\\d+\" selected=\"selected\">Meals</option>"));
        Assert.Equal(suggested ? 1 : 0, Regex.Count(page, "selected=\"selected\""));
    }

    [Fact]
    public async Task Failed_extraction_gives_the_empty_form_with_a_message_and_the_receipt_can_still_be_saved()
    {
        using var app = new KlaiminApp { Extraction = ExtractionResult.Failed };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        var confirmPage = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();
        var response = await client.ConfirmAsync(claim, confirmPage, new()
        {
            ["Total"] = "53.000",
            ["Date"] = "2026-09-14",
            ["CategoryId"] = HttpClientExtensions.CategoryId(confirmPage, "Meals"),
        });

        Assert.Contains("The receipt could not be read automatically. Enter the fields yourself.", confirmPage);
        Assert.Matches("<input[^>]*name=\"Total\"[^>]*value=\"\"", confirmPage);
        Assert.DoesNotContain("selected=\"selected\"", confirmPage);
        Assert.Contains("Rp 53.000", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_no_API_key_the_app_runs_on_manual_entry()
    {
        // Outside development no user secrets are read, so no key can leak in from the machine running the tests.
        using var app = new KlaiminApp { OwnExtractor = true, EnvironmentName = "Production" };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();

        var confirmPage = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();
        var response = await client.ConfirmAsync(claim, confirmPage, new()
        {
            ["Total"] = "53.000",
            ["Date"] = "2026-09-14",
            ["CategoryId"] = HttpClientExtensions.CategoryId(confirmPage, "Meals"),
        });

        Assert.Contains("Automatic reading is not set up on this server. Enter the fields yourself.", confirmPage);
        Assert.Contains("Rp 53.000", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_failed_confirmation_keeps_the_photo_and_what_was_typed()
    {
        using var app = new KlaiminApp { Extraction = Extracted() };
        var client = await app.SignedInAsync("claimant");
        var claim = await client.StartClaimAsync();
        var confirmPage = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();

        var response = await client.ConfirmAsync(claim, confirmPage, new()
        {
            ["Total"] = "lima puluh ribu",
            ["Date"] = "2026-09-14",
            ["CategoryId"] = HttpClientExtensions.CategoryId(confirmPage, "Meals"),
            ["LineItems[0].Name"] = "Nasi goreng",
            ["LineItems[0].Price"] = "45.000",
        });

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter the total in whole Rupiah, for example 125.000.", page);
        Assert.Contains("<img src=\"/Receipts/Pending/", page);
        Assert.Matches("<input[^>]*name=\"LineItems\\[0\\].Name\"[^>]*value=\"Nasi goreng\"", page);
        Assert.Contains("No receipts yet.", await client.GetStringAsync(claim));
    }

    [Fact]
    public async Task An_upload_can_only_be_confirmed_by_the_person_who_made_it_and_only_once()
    {
        using var app = new KlaiminApp { Extraction = Extracted() };
        var claimant = await app.SignedInAsync("claimant");
        var claim = await claimant.StartClaimAsync();
        var confirmPage = await (await claimant.UploadAsync(claim)).Content.ReadAsStringAsync();
        Dictionary<string, string> Fields() => new()
        {
            ["Total"] = "53.000",
            ["Date"] = "2026-09-14",
            ["CategoryId"] = HttpClientExtensions.CategoryId(confirmPage, "Meals"),
        };
        var manager = await app.SignedInAsync("manager");
        var managersClaim = await manager.StartClaimAsync("Taxi to the airport");

        var forged = await claimant.ConfirmAsync(claim, "name=\"Upload\" value=\"not-a-real-upload\"", Fields());
        var stolen = await manager.ConfirmAsync(managersClaim, confirmPage, Fields());
        var first = await claimant.ConfirmAsync(claim, confirmPage, Fields());
        var second = await claimant.ConfirmAsync(claim, confirmPage, Fields());

        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Equal(claim, first.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("This photo is already saved as a receipt.", await second.Content.ReadAsStringAsync());
        Assert.Single(Regex.Matches(await claimant.GetStringAsync(claim), "<article class=\"receipt\">"));
    }
}
