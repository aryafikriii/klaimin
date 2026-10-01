using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Klaimin.Tests;

/// <summary>The real app, in memory, on its own temporary SQLite file and image folder.</summary>
public sealed class KlaiminApp : WebApplicationFactory<Program>
{
    public const string Password = "Receipt-2026!";

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"klaimin-{Guid.NewGuid():N}.db");
    private readonly string _images = Path.Combine(Path.GetTempPath(), $"klaimin-{Guid.NewGuid():N}");

    public string EnvironmentName { get; init; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // Pooling off so the file is released and can be deleted on dispose.
        builder.UseSetting("ConnectionStrings:Klaimin", $"Data Source={_database};Pooling=False");
        builder.UseSetting("Seed:Password", Password);
        builder.UseSetting("Storage:ReceiptImages", _images);
    }

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

    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password = KlaiminApp.Password) =>
        client.PostFormAsync("/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = password });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
