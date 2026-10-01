using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class ClaimsController : Controller
{
    public IActionResult Index() => View();
}
