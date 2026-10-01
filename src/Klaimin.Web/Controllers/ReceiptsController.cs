using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Klaimin.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class UploadForm
{
    [Required(ErrorMessage = "Choose a photo of the receipt.")]
    public IFormFile? Image { get; set; }
}

public class LineItemForm
{
    public string? Name { get; set; }
    public string? Price { get; set; }
}

public class ReceiptForm
{
    /// <summary>Names the uploaded image this form confirms. Sealed so it cannot be pointed at another file or claim.</summary>
    public string Upload { get; set; } = "";

    public string? Total { get; set; }

    [Required(ErrorMessage = "Enter the date printed on the receipt.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Choose a category from the list.")]
    public int? CategoryId { get; set; }

    public List<LineItemForm> LineItems { get; set; } = [];
}

public class ReceiptsController(
    ClaimService claims, ReceiptImages images, IReceiptExtractor extractor, IDataProtectionProvider protection) : Controller
{
    private const int BlankRows = 3;

    private readonly IDataProtector _uploads = protection.CreateProtector("Klaimin.ReceiptUpload");

    [HttpGet]
    public async Task<IActionResult> New(int id) =>
        await EditableClaimAsync(id) is null ? NotFound() : View(new UploadForm());

    [HttpPost]
    public async Task<IActionResult> New(int id, UploadForm upload, CancellationToken cancellation)
    {
        if (await EditableClaimAsync(id) is null) return NotFound();
        if (!ModelState.IsValid) return View(upload);

        await using var stream = upload.Image!.OpenReadStream();
        var (image, problem) = await images.TryStoreAsync(stream, upload.Image.Length);
        if (image is null)
        {
            ModelState.AddModelError(problem!.Field, problem.Message);
            return View(upload);
        }

        var categories = await claims.ActiveCategoriesAsync();
        var bytes = await System.IO.File.ReadAllBytesAsync(images.PathOf(image.File), cancellation);
        var result = await extractor.ExtractAsync(
            bytes, image.ContentType, [.. categories.Select(category => category.Name)], cancellation);
        var extraction = result.Extraction;

        ViewData["Notice"] = result.Outcome switch
        {
            ExtractionOutcome.Extracted => "These fields were read from the photo. Check each one against the receipt before you confirm.",
            ExtractionOutcome.Failed => "The receipt could not be read automatically. Enter the fields yourself.",
            _ => "Automatic reading is not set up on this server. Enter the fields yourself.",
        };
        var form = new ReceiptForm
        {
            Upload = _uploads.Protect($"{User.Id()}|{id}|{image.File}|{image.ContentType}|{image.Hash}"),
            Total = extraction?.Total?.ToString("N0", Rupiah.Dots),
            Date = extraction?.Date,
            // The model's answer only counts when it names a category that exists and is active.
            CategoryId = categories
                .FirstOrDefault(category => category.Name.Equals(extraction?.Category?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id,
            LineItems = [.. (extraction?.LineItems ?? [])
                .Select(item => new LineItemForm { Name = item.Name, Price = item.Price.ToString("N0", Rupiah.Dots) })],
        };
        return await ConfirmFormAsync(form);
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(int id, ReceiptForm form)
    {
        var claim = await EditableClaimAsync(id);
        var image = Unseal(form.Upload, id);
        if (claim is null || image is null) return NotFound();

        var entry = ReadEntry(form);
        if (entry is null) return await ConfirmFormAsync(form);

        var problem = await claims.AddReceiptAsync(claim, User.Id(), entry, image);
        if (problem is null) return RedirectToAction("Details", "Claims", new { id });

        ModelState.AddModelError(problem.Field, problem.Message);
        return await ConfirmFormAsync(form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (await EditableReceiptAsync(id) is not var (_, receipt)) return NotFound();

        return await FormAsync("Edit", new ReceiptForm
        {
            Total = receipt.Total.ToString("N0", Rupiah.Dots),
            Date = receipt.Date,
            CategoryId = receipt.CategoryId,
            LineItems = [.. receipt.LineItems
                .Select(item => new LineItemForm { Name = item.Name, Price = item.Price.ToString("N0", Rupiah.Dots) })],
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ReceiptForm form)
    {
        if (await EditableReceiptAsync(id) is not var (claim, receipt)) return NotFound();

        var entry = ReadEntry(form);
        if (entry is null) return await FormAsync("Edit", form);

        var problem = await claims.UpdateReceiptAsync(claim, User.Id(), receipt, entry);
        if (problem is null) return RedirectToAction("Details", "Claims", new { id = claim.Id });

        ModelState.AddModelError(problem.Field, problem.Message);
        return await FormAsync("Edit", form);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id)
    {
        if (await EditableReceiptAsync(id) is not var (claim, receipt)) return NotFound();

        await claims.RemoveReceiptAsync(claim, User.Id(), receipt);
        return RedirectToAction("Details", "Claims", new { id = claim.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Justify(int id, string? justification)
    {
        if (await EditableReceiptAsync(id) is not var (claim, receipt) || receipt.ExceededCap is null) return NotFound();

        var problem = await claims.JustifyAsync(claim, User.Id(), receipt, justification);
        if (problem is null) return RedirectToAction("Details", "Claims", new { id = claim.Id });

        ModelState.AddModelError(problem.Field, problem.Message);
        return View("~/Views/Claims/Details.cshtml", ClaimPage.For(claim, User));
    }

    /// <summary>The image of an upload that is not a receipt yet, for the person who uploaded it.</summary>
    public IActionResult Pending(int id, string upload)
    {
        var image = Unseal(upload, id);
        return image is null ? NotFound() : PhysicalFile(images.PathOf(image.File), image.ContentType);
    }

    public async Task<IActionResult> Image(int id)
    {
        return await claims.FindReceiptAsync(id, User.AsViewer()) is var (_, receipt)
            ? PhysicalFile(images.PathOf(receipt.ImageFile), receipt.ImageContentType)
            : NotFound();
    }

    private async Task<Claim?> EditableClaimAsync(int id)
    {
        var claim = await claims.FindAsync(id, User.AsViewer());
        return claim?.CanBeEditedBy(User.Id()) == true ? claim : null;
    }

    private async Task<(Claim Claim, Receipt Receipt)?> EditableReceiptAsync(int receiptId) =>
        await claims.FindReceiptAsync(receiptId, User.AsViewer()) is var (claim, receipt) && claim.CanBeEditedBy(User.Id())
            ? (claim, receipt)
            : null;

    /// <summary>The typed fields as an entry, or null after recording what is wrong with them.</summary>
    private ReceiptEntry? ReadEntry(ReceiptForm form)
    {
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

        return ModelState.IsValid ? new ReceiptEntry(total, form.Date!.Value, form.CategoryId!.Value, lineItems) : null;
    }

    private StoredImage? Unseal(string? upload, int claimId)
    {
        try
        {
            return _uploads.Unprotect(upload ?? "").Split('|') is [var user, var claim, var file, var contentType, var hash]
                && user == User.Id() && claim == claimId.ToString()
                    ? new StoredImage(file, contentType, hash)
                    : null;
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private Task<IActionResult> ConfirmFormAsync(ReceiptForm form) => FormAsync("Confirm", form);

    private async Task<IActionResult> FormAsync(string view, ReceiptForm form)
    {
        while (form.LineItems.Count < BlankRows || !string.IsNullOrWhiteSpace(form.LineItems[^1].Name + form.LineItems[^1].Price))
            form.LineItems.Add(new LineItemForm());
        ViewData["Categories"] = await claims.ActiveCategoriesAsync();
        return View(view, form);
    }
}
