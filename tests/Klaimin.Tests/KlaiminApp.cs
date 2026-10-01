using System.Net;
using System.Text.RegularExpressions;
using Klaimin.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Klaimin.Tests;

/// <summary>The real app, in memory, on its own temporary SQLite file and image folder, with extraction answered by the test.</summary>
public sealed class KlaiminApp : WebApplicationFactory<Program>
{
    public const string Password = "Receipt-2026!";

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"klaimin-{Guid.NewGuid():N}.db");
    private readonly string _images = Path.Combine(Path.GetTempPath(), $"klaimin-{Guid.NewGuid():N}");

    public string EnvironmentName { get; init; } = "Development";

    /// <summary>What the fake extraction adapter answers for every upload.</summary>
    public ExtractionResult Extraction { get; set; } = ExtractionResult.Failed;

    /// <summary>Leaves the app's own extraction adapter in place instead of the fake.</summary>
    public bool OwnExtractor { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // Pooling off so the file is released and can be deleted on dispose.
        builder.UseSetting("ConnectionStrings:Klaimin", $"Data Source={_database};Pooling=False");
        builder.UseSetting("Seed:Password", Password);
        builder.UseSetting("Storage:ReceiptImages", _images);
        if (!OwnExtractor)
            builder.ConfigureTestServices(services => services.AddSingleton<IReceiptExtractor>(new FakeExtractor(this)));
    }

    private sealed class FakeExtractor(KlaiminApp app) : IReceiptExtractor
    {
        public Task<ExtractionResult> ExtractAsync(
            byte[] image, string contentType, IReadOnlyList<string> categories, CancellationToken cancellation = default) =>
            Task.FromResult(app.Extraction);
    }

    /// <summary>How many receipt images are on disk. The one thing a page cannot show.</summary>
    public int StoredImageCount => Directory.Exists(_images) ? Directory.GetFiles(_images).Length : 0;

    public async Task<HttpClient> SignedInAsync(string role)
    {
        var client = CreateClient();
        await client.SignInAsync($"{role}@klaimin.test");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_database);
        if (Directory.Exists(_images)) Directory.Delete(_images, recursive: true);
    }
}

public static partial class HttpClientExtensions
{
    /// <summary>Posts a form the way a browser would: loads the page that holds it, then sends its antiforgery token along.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string page, string action, Dictionary<string, string>? fields = null,
        (string Field, string FileName, byte[] Bytes)? file = null)
    {
        var html = await client.GetStringAsync(page);
        var token = AntiforgeryToken().Match(html).Groups[1].Value;
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) };
        if (file is null) return await client.PostAsync(action, new FormUrlEncodedContent(form));

        var multipart = new MultipartFormDataContent();
        foreach (var (name, value) in form) multipart.Add(new StringContent(value), name);
        multipart.Add(new ByteArrayContent(file.Value.Bytes), file.Value.Field, file.Value.FileName);
        return await client.PostAsync(action, multipart);
    }

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>Starts a claim and returns the address of its page.</summary>
    public static async Task<string> StartClaimAsync(this HttpClient client, string title = "Client visit to Bandung")
    {
        var response = await client.PostFormAsync("/Claims/New", "/Claims/New", new() { ["Title"] = title });
        return response.RequestMessage!.RequestUri!.AbsolutePath;
    }

    public static string UploadForm(string claim) => claim.Replace("/Claims/Details/", "/Receipts/New/");

    public static Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, string claim, byte[]? image = null, string fileName = "receipt.png") =>
        client.PostFormAsync(UploadForm(claim), UploadForm(claim), file: ("Image", fileName, image ?? Png));

    /// <summary>Confirms the upload shown on a confirmation page with the given fields.</summary>
    public static Task<HttpResponseMessage> ConfirmAsync(
        this HttpClient client, string claim, string confirmPage, Dictionary<string, string> fields)
    {
        fields["Upload"] = WebUtility.HtmlDecode(UploadToken().Match(confirmPage).Groups[1].Value);
        return client.PostFormAsync(claim, claim.Replace("/Claims/Details/", "/Receipts/Confirm/"), fields);
    }

    /// <summary>Uploads a receipt image and confirms it with fields typed by hand.</summary>
    public static async Task<HttpResponseMessage> AddReceiptAsync(
        this HttpClient client, string claim, string total = "125.000", string category = "Meals")
    {
        var page = await (await client.UploadAsync(claim)).Content.ReadAsStringAsync();
        return await client.ConfirmAsync(claim, page, new()
        {
            ["Total"] = total,
            ["Date"] = "2026-09-14",
            ["CategoryId"] = CategoryId(page, category),
        });
    }

    public static Task<HttpResponseMessage> SubmitAsync(this HttpClient client, string claim) =>
        client.PostFormAsync(claim, claim.Replace("Details", "Submit"));

    /// <summary>Decides a claim as an approver would from the claim page: kind is Approve, Return, or Reject.</summary>
    public static Task<HttpResponseMessage> DecideAsync(
        this HttpClient client, string claim, string kind, string comment = "") =>
        client.PostFormAsync("/", claim.Replace("Details", "Decide"), new() { ["kind"] = kind, ["comment"] = comment });

    /// <summary>The id of a category as offered on a confirmation page.</summary>
    public static string CategoryId(string confirmPage, string category) =>
        Regex.Match(confirmPage, $"<option value=\"(\\d+)\"(?: selected=\"selected\")?>{category}</option>").Groups[1].Value;

    [GeneratedRegex("name=\"Upload\" value=\"([^\"]+)\"")]
    private static partial Regex UploadToken();

    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password = KlaiminApp.Password) =>
        client.PostFormAsync("/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = password });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
