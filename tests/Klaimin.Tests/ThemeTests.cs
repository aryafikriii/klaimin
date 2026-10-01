namespace Klaimin.Tests;

public class ThemeTests
{
    [Fact]
    public async Task Pages_are_light_until_the_dark_theme_is_switched_on()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();

        var before = await client.GetStringAsync("/Account/Login");
        var response = await client.PostFormAsync(
            "/Account/Login", "/Theme/Set", new() { ["theme"] = "dark", ["returnUrl"] = "/Account/Login" });

        var after = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"light\"", before);
        Assert.Contains("data-theme=\"dark\"", after);
        Assert.Contains("aria-checked=\"true\"", after);
    }

    [Fact]
    public async Task Switching_the_dark_theme_off_brings_back_light()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();
        await client.PostFormAsync(
            "/Account/Login", "/Theme/Set", new() { ["theme"] = "dark", ["returnUrl"] = "/Account/Login" });

        var response = await client.PostFormAsync(
            "/Account/Login", "/Theme/Set", new() { ["theme"] = "light", ["returnUrl"] = "/Account/Login" });

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"light\"", page);
        Assert.Contains("aria-checked=\"false\"", page);
    }

    [Fact]
    public async Task Pages_link_an_icon_that_is_served_without_signing_in()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient();

        var page = await client.GetStringAsync("/Account/Login");
        var icon = await client.GetAsync("/favicon.svg");

        Assert.Contains("<link rel=\"icon\" href=\"/favicon.svg\"", page);
        Assert.Equal("image/svg+xml", icon.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Switching_theme_never_leaves_the_site()
    {
        using var app = new KlaiminApp();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostFormAsync(
            "/Account/Login", "/Theme/Set", new() { ["theme"] = "dark", ["returnUrl"] = "https://example.com/" });

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }
}
