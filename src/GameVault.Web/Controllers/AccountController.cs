using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using GameVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    [HttpGet("register")]
    public IActionResult Register(string? returnUrl = null) => View(new RegisterModel { }).WithReturn(this, returnUrl);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model).WithReturn(this, returnUrl);

        var user = new AppUser { UserName = model.Email, Email = model.Email, DisplayName = model.DisplayName.Trim() };
        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model).WithReturn(this, returnUrl);
        }

        await _users.AddToRoleAsync(user, Roles.Member);
        await _signIn.SignInAsync(user, isPersistent: false);
        return Back(returnUrl);
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => View(new LoginModel()).WithReturn(this, returnUrl);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model).WithReturn(this, returnUrl);

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return Back(returnUrl);

        // One message for unknown email and wrong password, so the form cannot be used to find accounts.
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Too many failed attempts. Try again in a few minutes."
            : "Invalid email or password.");
        return View(model).WithReturn(this, returnUrl);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await _users.GetUserAsync(User);
        return user is null ? Challenge() : View(new ProfileModel { DisplayName = user.DisplayName, Email = user.Email ?? "" });
    }

    [Authorize, HttpPost("profile")]
    public async Task<IActionResult> Profile(ProfileModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        model.Email = user.Email ?? "";
        if (!ModelState.IsValid) return View(model);

        user.DisplayName = model.DisplayName.Trim();
        await _users.UpdateAsync(user);
        await _signIn.RefreshSignInAsync(user); // so the new name shows up without signing in again
        TempData["Message"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();

    private IActionResult Back(string? returnUrl)
        => Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
}

internal static class ViewResultExtensions
{
    public static IActionResult WithReturn(this ViewResult view, Controller c, string? returnUrl)
    {
        c.ViewData["ReturnUrl"] = returnUrl;
        return view;
    }
}
