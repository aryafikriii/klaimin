using Klaimin.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class SettingsForm
{
    public string? FinanceThreshold { get; set; }
}

[Authorize(Roles = Roles.Admin)]
public class SettingsController(PolicyService policy) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(new SettingsForm
    {
        FinanceThreshold = (await policy.FinanceThresholdAsync()).ToString("N0", Rupiah.Dots),
    });

    [HttpPost]
    public async Task<IActionResult> Index(SettingsForm form)
    {
        if (!Rupiah.TryParse(form.FinanceThreshold, out var threshold))
        {
            ModelState.AddModelError(nameof(form.FinanceThreshold), "Enter the threshold in whole Rupiah, for example 1.000.000.");
            return View(form);
        }

        if (await policy.SetFinanceThresholdAsync(threshold) is { } problem)
        {
            ModelState.AddModelError(problem.Field, problem.Message);
            return View(form);
        }

        TempData["Saved"] = $"The finance threshold is now {Rupiah.Format(threshold)}.";
        return RedirectToAction(nameof(Index));
    }
}
