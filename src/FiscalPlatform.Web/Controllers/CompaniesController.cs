using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using FiscalPlatform.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Controllers;

public class CompanyPageModel
{
    public required CompanyInput Input { get; init; }
    public int? Id { get; init; }
    public List<ReadinessItem> Readiness { get; init; } = [];
    public List<CertificateMetadata> Certificates { get; init; } = [];
    public int WarningDays { get; init; }
}

public class CompaniesController(AppDbContext db, CompanyEditor editor, FiscalOptions options) : AppController
{
    public async Task<IActionResult> Index()
    {
        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.LegalName).ToListAsync();
        if (companies.Count == 1) return RedirectToAction(nameof(Edit), new { id = companies[0].Id });
        ViewBag.ActiveId = ActiveCompany.ReadId(HttpContext);
        return View(companies);
    }

    [HttpPost]
    public async Task<IActionResult> Select(int id, string? returnUrl)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.IsActive);
        if (company is null) { Failure("Empresa não encontrada ou inativa."); return RedirectToAction(nameof(Index)); }
        ActiveCompany.Set(HttpContext, id);
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("New", "Documents");
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new CompanyPageModel
    {
        Input = new CompanyInput { Address = new Address { City = "Cuiabá", State = "MT", CityIbgeCode = Company.CuiabaIbgeCode } },
    });

    [HttpPost, ActionName("Create")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> CreatePost(CompanyInput input)
    {
        var company = new Company();
        var result = await editor.SaveAsync(company, input, UserName);
        if (!result.Success) return await FormAsync(input, null, result.Errors);
        ActiveCompany.Set(HttpContext, company.Id);
        Success("Empresa cadastrada e selecionada. " + result.Notice);
        return RedirectToAction(nameof(Edit), new { id = company.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return company is null ? NotFound() : await FormAsync(CompanyInput.From(company), company, []);
    }

    [HttpPost, ActionName("Edit")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> EditPost(int id, CompanyInput input)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company is null) return NotFound();
        var result = await editor.SaveAsync(company, input, UserName);
        if (!result.Success) return await FormAsync(input, company, result.Errors);
        Success("Dados salvos. " + result.Notice);
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        company.IsActive = !company.IsActive;
        Audit.Log(db, DateTime.Now, id, UserName, company.IsActive ? "Reativar empresa" : "Inativar empresa", "Empresa", id);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> FormAsync(CompanyInput input, Company? company, IEnumerable<ValidationError> errors)
    {
        ModelState.Clear();
        AddErrors(errors, "Input");
        var certificates = company is null ? [] : await db.Certificates.AsNoTracking()
            .Where(c => c.CompanyId == company.Id).OrderByDescending(c => c.ImportedAt).ToListAsync();
        return View("Form", new CompanyPageModel
        {
            Input = input,
            Id = company?.Id,
            Certificates = certificates,
            Readiness = company is null ? [] : CompanyReadiness.Evaluate(company, certificates.FirstOrDefault(c => c.IsActive), DateTime.Now, options.CertificateWarningDays),
            WarningDays = options.CertificateWarningDays,
        });
    }
}
