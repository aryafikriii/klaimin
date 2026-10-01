using Klaimin.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class CategoryForm
{
    public string? Name { get; set; }
    public string? Cap { get; set; }
}

[Authorize(Roles = Roles.Admin)]
public class CategoriesController(PolicyService policy) : Controller
{
    public async Task<IActionResult> Index() => View(await policy.CategoriesAsync());

    [HttpGet]
    public IActionResult New() => View("Form", new CategoryForm());

    [HttpPost]
    public async Task<IActionResult> New(CategoryForm form)
    {
        if (ReadCap(form) is not { } cap) return View("Form", form);

        return Done(await policy.AddCategoryAsync(form.Name, cap), form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await policy.FindCategoryAsync(id);
        return category is null
            ? NotFound()
            : View("Form", new CategoryForm { Name = category.Name, Cap = category.Cap.ToString("N0", Rupiah.Dots) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, CategoryForm form)
    {
        var category = await policy.FindCategoryAsync(id);
        if (category is null) return NotFound();
        if (ReadCap(form) is not { } cap) return View("Form", form);

        return Done(await policy.UpdateCategoryAsync(category, form.Name, cap), form);
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var category = await policy.FindCategoryAsync(id);
        if (category is null) return NotFound();

        await policy.SetActiveAsync(category, active);
        return RedirectToAction(nameof(Index));
    }

    private long? ReadCap(CategoryForm form)
    {
        if (Rupiah.TryParse(form.Cap, out var cap)) return cap;

        ModelState.AddModelError(nameof(form.Cap), "Enter the cap in whole Rupiah, for example 150.000.");
        return null;
    }

    private IActionResult Done(Problem? problem, CategoryForm form)
    {
        if (problem is null) return RedirectToAction(nameof(Index));

        ModelState.AddModelError(problem.Field, problem.Message);
        return View("Form", form);
    }
}
