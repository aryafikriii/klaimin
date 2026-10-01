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

public record ClaimPage(Claim Claim, bool Editable, bool Decidable, string? Comment = null);

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
        return claim is null ? NotFound() : View(Page(claim));
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        if (claim is null || !claim.CanBeEditedBy(User.Id())) return NotFound();

        var problem = await claims.SubmitAsync(claim, User.Id());
        if (problem is null) return RedirectToAction(nameof(Details), new { id });

        ModelState.AddModelError("", problem.Message);
        return View(nameof(Details), Page(claim));
    }

    [HttpPost]
    public async Task<IActionResult> Decide(int id, DecisionKind kind, string? comment)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        if (claim is null || !claim.CanBeDecidedBy(User.AsViewer()) || !Enum.IsDefined(kind)) return NotFound();

        var problem = await claims.DecideAsync(claim, User.AsViewer(), kind, comment);
        if (problem is null) return RedirectToAction("Index", "Approvals");

        ModelState.AddModelError(problem.Field, problem.Message);
        return View(nameof(Details), Page(claim, comment));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        if (claim is null || !claim.CanBeDeletedBy(User.Id())) return NotFound();

        await claims.DeleteAsync(claim, User.Id());
        return RedirectToAction(nameof(Index));
    }

    private ClaimPage Page(Claim claim, string? comment = null) =>
        new(claim, claim.CanBeEditedBy(User.Id()), claim.CanBeDecidedBy(User.AsViewer()), comment);
}
