using System.ComponentModel.DataAnnotations;
using Klaimin.Core;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class LineItemForm
{
    public string? Name { get; set; }
    public string? Price { get; set; }
}

public class ReceiptForm
{
    [Required(ErrorMessage = "Choose a photo of the receipt.")]
    public IFormFile? Image { get; set; }

    public string? Total { get; set; }

    [Required(ErrorMessage = "Enter the date printed on the receipt.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Choose a category from the list.")]
    public int? CategoryId { get; set; }

    public List<LineItemForm> LineItems { get; set; } = [new(), new(), new()];
}

public class ReceiptsController(ClaimService claims, ReceiptImages images) : Controller
{
    [HttpGet]
    public async Task<IActionResult> New(int id)
    {
        if (await EditableClaimAsync(id) is null) return NotFound();
        return await FormAsync(new ReceiptForm());
    }

    [HttpPost]
    public async Task<IActionResult> New(int id, ReceiptForm form)
    {
        var claim = await EditableClaimAsync(id);
        if (claim is null) return NotFound();

        if (!Rupiah.TryParse(form.Total, out var total))
            ModelState.AddModelError(nameof(form.Total), "Enter the total in whole Rupiah, for example 125.000.");

        var lineItems = new List<LineItem>();
        foreach (var row in form.LineItems.Where(row => !string.IsNullOrWhiteSpace(row.Name + row.Price)))
        {
            if (!string.IsNullOrWhiteSpace(row.Name) && Rupiah.TryParse(row.Price, out var price))
                lineItems.Add(new LineItem { Name = row.Name.Trim(), Price = price });
            else
                ModelState.TryAddModelError(nameof(form.LineItems), "Give each line item a name and a price in whole Rupiah, or leave the row empty.");
        }

        if (!ModelState.IsValid) return await FormAsync(form);

        await using var stream = form.Image!.OpenReadStream();
        var problem = await claims.AddReceiptAsync(
            claim, User.Id(), new ReceiptEntry(total, form.Date!.Value, form.CategoryId!.Value, lineItems), stream, form.Image.Length);
        if (problem is null) return RedirectToAction("Details", "Claims", new { id });

        ModelState.AddModelError(problem.Field, problem.Message);
        return await FormAsync(form);
    }

    public async Task<IActionResult> Image(int id)
    {
        var receipt = await claims.FindReceiptAsync(id, User.AsViewer());
        return receipt is null ? NotFound() : PhysicalFile(images.PathOf(receipt.ImageFile), receipt.ImageContentType);
    }

    private async Task<Claim?> EditableClaimAsync(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        return claim?.CanBeEditedBy(User.Id()) == true ? claim : null;
    }

    private async Task<IActionResult> FormAsync(ReceiptForm form)
    {
        ViewData["Categories"] = await claims.ActiveCategoriesAsync();
        return View(form);
    }
}
