using Klaimin.Core;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class ApprovalsController(ClaimService claims) : Controller
{
    public async Task<IActionResult> Index() => View(await claims.AwaitingAsync(User.AsViewer()));
}
