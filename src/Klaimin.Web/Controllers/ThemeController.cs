using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

[AllowAnonymous]
public class ThemeController : Controller
{
    public const string Cookie = "theme";

    [HttpPost]
    public IActionResult Set(string theme, string? returnUrl)
    {
        Response.Cookies.Append(Cookie, theme == "dark" ? "dark" : "light", new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(365),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
