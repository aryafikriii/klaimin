using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Klaimin.Tests;

/// <summary>The real app, in memory, on its own temporary SQLite file.</summary>
public sealed class KlaiminApp : WebApplicationFactory<Program>
{
    public const string Password = "Receipt-2026!";

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"klaimin-{Guid.NewGuid():N}.db");

    public string EnvironmentName { get; init; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // Pooling off so the file is released and can be deleted on dispose.
        builder.UseSetting("ConnectionStrings:Klaimin", $"Data Source={_database};Pooling=False");
        builder.UseSetting("Seed:Password", Password);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_database);
    }
}

public static partial class HttpClientExtensions
{
    /// <summary>Posts a form the way a browser would: loads the page that holds it, then sends its antiforgery token along.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string page, string action, Dictionary<string, string>? fields = null)
    {
        var html = await client.GetStringAsync(page);
        var token = AntiforgeryToken().Match(html).Groups[1].Value;
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) };
        return await client.PostAsync(action, new FormUrlEncodedContent(form));
    }

    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password = KlaiminApp.Password) =>
        client.PostFormAsync("/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = password });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
