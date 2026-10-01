using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Klaimin.Core;
using Microsoft.AspNetCore.Mvc;
using Claim = Klaimin.Core.Claim;

namespace Klaimin.Web.Controllers;

public class NewClaimForm
{
    [Required(ErrorMessage = "Give the claim a title."), StringLength(100, ErrorMessage = "Keep the title to 100 characters.")]
    public string? Title { get; set; }
}

public record ClaimPage(Claim Claim, bool Editable);

public static class ViewerExtensions
{
    public static string Id(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static Viewer AsViewer(this ClaimsPrincipal user) =>
        new(user.Id(), user.IsInRole(Roles.Finance), user.IsInRole(Roles.Admin));
}

public class ClaimsController(ClaimService claims) : Controller
{
    public async Task<IActionResult> Index() => View(await claims.MineAsync(User.Id()));

    [HttpGet]
    public IActionResult New() => View(new NewClaimForm());

    [HttpPost]
    public async Task<IActionResult> New(NewClaimForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var claim = await claims.StartAsync(User.Id(), form.Title!);
        return RedirectToAction(nameof(Details), new { id = claim.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        return claim is null ? NotFound() : View(new ClaimPage(claim, claim.CanBeEditedBy(User.Id())));
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        if (claim is null || !claim.CanBeEditedBy(User.Id())) return NotFound();

        var problem = await claims.SubmitAsync(claim, User.Id());
        if (problem is null) return RedirectToAction(nameof(Details), new { id });

        ModelState.AddModelError("", problem.Message);
        return View(nameof(Details), new ClaimPage(claim, Editable: true));
    }
}
