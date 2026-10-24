using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using FiscalPlatform.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Controllers;

/// <summary>Primeiro acesso (conta por e-mail + empresa no mesmo passo), login e saída.</summary>
[AllowAnonymous]
public class AccountController(AppDbContext db, PasswordHasher hasher, CompanyEditor companies) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Register() =>
        await Setup.IsCompleteAsync(db) ? RedirectToAction(nameof(Login)) : View(new RegisterInput());

    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (await Setup.IsCompleteAsync(db)) return RedirectToAction(nameof(Login));
        ModelState.Clear();
        var email = input.Email.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email)) ModelState.AddModelError(nameof(input.Email), "Informe um e-mail válido.");
        if (input.Password.Length < 8) ModelState.AddModelError(nameof(input.Password), "A senha precisa de pelo menos 8 caracteres.");
        if (!ModelState.IsValid) return View(input);

        // Conta já criada antes (sem empresa): reaproveita se a senha conferir.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && !hasher.Verify(input.Password, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(input.Password), "Este e-mail já tem conta. Informe a senha dela para continuar.");
            return View(input);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (user is null)
        {
            user = new User { Email = email, PasswordHash = hasher.Hash(input.Password), CreatedAt = DateTime.Now };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var company = new Company();
        if (string.IsNullOrWhiteSpace(input.Company.Email)) input.Company.Email = email;
        var result = await companies.SaveAsync(company, input.Company, email);
        if (!result.Success)
        {
            await transaction.RollbackAsync();
            foreach (var e in result.Errors) ModelState.AddModelError($"{nameof(input.Company)}.{e.Field}", e.Message);
            return View(input);
        }
        Audit.Log(db, DateTime.Now, company.Id, email, "Criar conta", "Usuário", email);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Setup.MarkComplete();
        ActiveCompany.Set(HttpContext, company.Id);
        await SignInAsync(user);
        TempData["Message"] = "Tudo pronto! " + (result.Notice ?? "") + " Cadastre seus clientes e serviços e emita a primeira nota.";
        return RedirectToAction("New", "Documents");
    }

    [HttpGet]
    public async Task<IActionResult> Login()
    {
        ViewBag.CanRegister = !await Setup.IsCompleteAsync(db);
        return View(new LoginInput());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginInput input, string? returnUrl)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !hasher.Verify(input.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "E-mail ou senha incorretos.");
            ViewBag.CanRegister = !await Setup.IsCompleteAsync(db);
            return View(input);
        }
        await SignInAsync(user);
        Audit.Log(db, DateTime.Now, null, user.Email, "Login", "Usuário", user.Email);
        await db.SaveChangesAsync();
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("New", "Documents");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private Task SignInAsync(User user)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.Email)], CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
