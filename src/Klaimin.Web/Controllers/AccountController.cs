using System.ComponentModel.DataAnnotations;
using Klaimin.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Klaimin.Web.Controllers;

public class LoginForm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

[AllowAnonymous]
public class AccountController(SignInManager<AppUser> signIn, IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    public IActionResult Login() => View(new LoginForm());

    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var result = await signIn.PasswordSignInAsync(form.Email, form.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded) return RedirectToAction("Index", "Claims");

        ModelState.AddModelError("", result.IsLockedOut
            ? "Too many failed attempts. Wait five minutes and try again."
            : "That email and password do not match an account.");
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> DevLogin(string email)
    {
        if (!environment.IsDevelopment()) return NotFound();

        var user = await signIn.UserManager.FindByEmailAsync(email);
        if (user is null) return NotFound();

        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Claims");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
}
