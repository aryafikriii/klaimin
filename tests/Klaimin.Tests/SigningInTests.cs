using System.Net;

namespace Klaimin.Tests;

public class SigningInTests
{
    [Fact]
    public async Task Anonymous_visitor_is_sent_to_sign_in()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("claimant@klaimin.test")]
    [InlineData("manager@klaimin.test")]
    [InlineData("finance@klaimin.test")]
    [InlineData("finance2@klaimin.test")]
    [InlineData("admin@klaimin.test")]
    public async Task Seeded_account_signs_in_to_an_empty_My_claims(string email)
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();

        var response = await client.SignInAsync(email);

        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal("/", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("<h1>My claims</h1>", page);
        Assert.Contains("You have no claims yet.", page);
        Assert.Contains(email, page);
    }

    [Fact]
    public async Task Wrong_password_stays_on_sign_in_with_a_message()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();

        var response = await client.SignInAsync("claimant@klaimin.test", "not-the-password");

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("That email and password do not match an account.", page);
        Assert.DoesNotContain("My claims", page);
    }

    [Fact]
    public async Task Signing_out_returns_to_sign_in_and_ends_the_session()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();
        await client.SignInAsync("claimant@klaimin.test");

        var response = await client.PostFormAsync("/", "/Account/Logout");

        Assert.Equal("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
        var next = await client.GetAsync("/");
        Assert.Equal("/Account/Login", next.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Development_offers_one_click_sign_in_for_each_seeded_account()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();

        var signIn = await client.GetStringAsync("/Account/Login");
        var response = await client.PostFormAsync(
            "/Account/Login", "/Account/DevLogin", new() { ["email"] = "finance@klaimin.test" });

        foreach (var role in new[] { "claimant", "manager", "finance", "finance2", "admin" })
            Assert.Contains($"value=\"{role}@klaimin.test\"", signIn);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("<h1>My claims</h1>", page);
        Assert.Contains("finance@klaimin.test", page);
    }

    [Fact]
    public async Task Outside_development_there_is_no_one_click_sign_in()
    {
        using var app = new KlaiminApp { EnvironmentName = "Production" };
        var client = app.CreateClient();

        var signIn = await client.GetStringAsync("/Account/Login");
        var response = await client.PostFormAsync(
            "/Account/Login", "/Account/DevLogin", new() { ["email"] = "finance@klaimin.test" });

        Assert.DoesNotContain("@klaimin.test", signIn);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
