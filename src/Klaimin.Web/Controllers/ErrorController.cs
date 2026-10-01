using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class ErrorController : Controller
{
    public IActionResult Index() => View();
}
