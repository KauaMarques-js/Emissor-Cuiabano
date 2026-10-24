using FiscalPlatform.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Infrastructure;

/// <summary>Seletor da empresa ativa exibido no topo de todas as páginas.</summary>
public class CompanySelectorViewComponent(AppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        ViewBag.ActiveId = ActiveCompany.ReadId(HttpContext);
        var companies = await db.Companies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.LegalName).ToListAsync();
        return View(companies);
    }
}
