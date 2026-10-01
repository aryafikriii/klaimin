using System.Net;
using System.Text.RegularExpressions;

namespace Klaimin.Tests;

public class ClaimTests
{
    private static readonly byte[] Png = HttpClientExtensions.Png;
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    private static readonly byte[] WebP = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 1, 2];

    private static Task<string> StartClaimAsync(HttpClient client, string title = "Client visit to Bandung") =>
        client.StartClaimAsync(title);

    private static string ReceiptForm(string claim) => HttpClientExtensions.UploadForm(claim);

    private static string Submit(string claim) => claim.Replace("Details", "Submit");

    /// <summary>Uploads an image and confirms it by hand. Returns the upload response when the upload itself is refused.</summary>
    private static async Task<HttpResponseMessage> AddReceiptAsync(
        HttpClient client, string claim, byte[]? image = null, string fileName = "receipt.png",
        string total = "125.000", string category = "Meals")
    {
        var uploaded = await client.UploadAsync(claim, image ?? Png, fileName);
        var page = await uploaded.Content.ReadAsStringAsync();
        if (!page.Contains("name=\"Upload\"")) return uploaded;

        return await client.ConfirmAsync(claim, page, new()
        {
            ["Total"] = total,
            ["Date"] = "2026-09-14",
            ["CategoryId"] = HttpClientExtensions.CategoryId(page, category),
            ["LineItems[0].Name"] = "Nasi goreng",
            ["LineItems[0].Price"] = "45.000",
            ["LineItems[1].Name"] = "Es teh",
            ["LineItems[1].Price"] = "8000",
        });
    }

    private static string ImageAddress(string claimPage) =>
        Regex.Match(claimPage, "src=\"(/Receipts/Image/\\d+)\"").Groups[1].Value;

    [Fact]
    public async Task Claimant_starts_a_draft_claim_with_a_title()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");

        var claim = await StartClaimAsync(client);

        var page = await client.GetStringAsync(claim);
        Assert.Contains("Client visit to Bandung", page);
        Assert.Contains("Draft", page);
        Assert.Contains("Rp 0", page);
    }

    [Fact]
    public async Task A_claim_needs_a_title()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");

        var response = await client.PostFormAsync("/Claims/New", "/Claims/New", new() { ["Title"] = " " });

        Assert.Equal("/Claims/New", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Give the claim a title.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Receipt_entered_by_hand_shows_on_the_claim_with_its_fields()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);

        var response = await AddReceiptAsync(client, claim);

        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Rp 125.000", page);
        Assert.Contains("14 Sep 2026", page);
        Assert.Contains("Meals", page);
        Assert.Contains("Nasi goreng", page);
        Assert.Contains("Rp 45.000", page);
        Assert.Contains("Es teh", page);
        Assert.Contains("Rp 8.000", page);
    }

    [Theory]
    [InlineData("", "Enter the total in whole Rupiah, for example 125.000.")]
    [InlineData("12,5", "Enter the total in whole Rupiah, for example 125.000.")]
    [InlineData("0", "The receipt total must be more than Rp 0.")]
    public async Task Receipt_total_must_be_whole_Rupiah_above_zero(string total, string message)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);

        var response = await AddReceiptAsync(client, claim, total: total);

        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.Contains("No receipts yet.", await client.GetStringAsync(claim));
    }

    [Theory]
    [InlineData("Meals")]
    [InlineData("Transport")]
    [InlineData("Lodging")]
    [InlineData("Office supplies")]
    [InlineData("Other")]
    public async Task Seeded_category_can_be_chosen_for_a_receipt(string category)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);

        var response = await AddReceiptAsync(client, claim, category: category);

        Assert.Equal(claim, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains(category, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task Upload_accepts_JPEG_PNG_and_WebP_by_content_and_serves_the_image_back(string contentType)
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);
        var bytes = contentType switch { "image/jpeg" => Jpeg, "image/png" => Png, _ => WebP };

        var page = await (await AddReceiptAsync(client, claim, bytes, "photo.bin")).Content.ReadAsStringAsync();

        var image = await client.GetAsync(ImageAddress(page));
        Assert.Equal(contentType, image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_rejects_a_file_that_is_not_an_image_whatever_its_name()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);

        var response = await AddReceiptAsync(client, claim, "%PDF-1.7 not an image"u8.ToArray(), "receipt.png");

        Assert.Contains("The file is not a JPEG, PNG, or WebP image.", await response.Content.ReadAsStringAsync());
        Assert.Contains("No receipts yet.", await client.GetStringAsync(claim));
    }

    [Fact]
    public async Task Upload_rejects_an_image_above_5_MB()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);
        var tooLarge = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(tooLarge, 0);

        var response = await AddReceiptAsync(client, claim, tooLarge);

        Assert.Contains("The image is larger than 5 MB.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Empty_claim_cannot_be_submitted()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);

        var response = await client.PostFormAsync(claim, Submit(claim));

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add at least one receipt before submitting.", page);
        Assert.Contains("Draft", page);
    }

    [Fact]
    public async Task Submitted_claim_awaits_the_manager_and_is_read_only()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(client);
        await AddReceiptAsync(client, claim);

        var response = await client.PostFormAsync(claim, Submit(claim));

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Awaiting manager", page);
        Assert.DoesNotContain("Add receipt", page);
        Assert.DoesNotContain("Submit claim", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ReceiptForm(claim))).StatusCode);
        var late = await client.PostFormAsync(claim, ReceiptForm(claim), file: ("Image", "receipt.png", Png));
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync(claim, Submit(claim))).StatusCode);
    }

    [Fact]
    public async Task My_claims_lists_each_claim_with_status_and_claim_total()
    {
        using var app = new KlaiminApp();
        var client = await app.SignedInAsync("claimant");
        var submitted = await StartClaimAsync(client, "Client visit to Bandung");
        await AddReceiptAsync(client, submitted, total: "950.000", category: "Lodging");
        await client.PostFormAsync(submitted, Submit(submitted));
        await StartClaimAsync(client, "Printer paper");

        var page = await client.GetStringAsync("/");

        Assert.Matches(
            "(?s)Printer paper.*Draft.*Rp 0.*Client visit to Bandung.*Awaiting manager.*Rp 950\\.000", page);
        Assert.DoesNotContain("You have no claims yet.", page);
    }

    [Fact]
    public async Task My_claims_shows_only_my_own_claims()
    {
        using var app = new KlaiminApp();
        await StartClaimAsync(await app.SignedInAsync("manager"), "Taxi to the airport");
        var claimant = await app.SignedInAsync("claimant");

        Assert.DoesNotContain("Taxi to the airport", await claimant.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("finance")]
    [InlineData("admin")]
    public async Task Claim_and_image_open_for_the_manager_finance_and_admins_once_submitted(string role)
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(claimant);
        var image = ImageAddress(await (await AddReceiptAsync(claimant, claim)).Content.ReadAsStringAsync());
        var other = await app.SignedInAsync(role);

        var whileDraft = await other.GetAsync(image);
        await claimant.PostFormAsync(claim, Submit(claim));

        Assert.Equal(HttpStatusCode.NotFound, whileDraft.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(claim)).StatusCode);
        Assert.Equal(Png, await other.GetByteArrayAsync(image));
    }

    [Fact]
    public async Task Another_persons_claim_and_image_are_not_found()
    {
        using var app = new KlaiminApp();
        var manager = await app.SignedInAsync("manager");
        var claim = await StartClaimAsync(manager, "Taxi to the airport");
        var image = ImageAddress(await (await AddReceiptAsync(manager, claim)).Content.ReadAsStringAsync());
        await manager.PostFormAsync(claim, Submit(claim));
        var claimant = await app.SignedInAsync("claimant");

        Assert.Equal(HttpStatusCode.NotFound, (await claimant.GetAsync(claim)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await claimant.GetAsync(image)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await claimant.GetAsync(ReceiptForm(claim))).StatusCode);
    }

    [Fact]
    public async Task Receipt_image_is_not_reachable_without_signing_in()
    {
        using var app = new KlaiminApp();
        var claimant = await app.SignedInAsync("claimant");
        var claim = await StartClaimAsync(claimant);
        var image = ImageAddress(await (await AddReceiptAsync(claimant, claim)).Content.ReadAsStringAsync());

        var response = await app.CreateClient().GetAsync(image);

        Assert.Equal("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
