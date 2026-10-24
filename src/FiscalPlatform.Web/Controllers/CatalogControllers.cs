using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Controllers;

[RequireCompany]
public class CustomersController(AppDbContext db) : AppController
{
    private IQueryable<Customer> Query(string? q, bool inactive)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.CompanyId == CompanyId && (inactive || c.IsActive));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var digits = DocumentNumbers.Digits(q);
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{q}%") || (digits.Length > 0 && c.Document.Contains(digits)));
        }
        return query.OrderBy(c => c.Name);
    }

    public async Task<IActionResult> Index(string? q, bool inactive = false, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Inactive = inactive;
        return View(await Query(q, inactive).ToPagedAsync(page, 20));
    }

    public async Task<IActionResult> Export(string? q, bool inactive = false)
    {
        var rows = (await Query(q, inactive).ToListAsync()).Select(c => new object?[]
        {
            c.Document, c.Name, c.IeIndicator, c.StateRegistration, c.MunicipalRegistration, c.Email, c.Phone,
            c.Address.ZipCode, c.Address.Street, c.Address.Number, c.Address.Complement, c.Address.District,
            c.Address.City, c.Address.State, c.Address.CityIbgeCode, c.IsActive ? "Ativo" : "Inativo",
        });
        return Csv("clientes.csv", rows.Prepend(["CPF/CNPJ", "Nome", "Ind. IE", "IE", "IM", "E-mail", "Telefone", "CEP", "Logradouro",
            "Número", "Complemento", "Bairro", "Município", "UF", "IBGE", "Situação"]));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new Customer { Address = new Address { State = Company.Address.State } });

    [HttpPost, ActionName("Create")]
    public async Task<IActionResult> CreatePost()
    {
        var customer = new Customer { CompanyId = CompanyId, CreatedAt = DateTime.Now };
        return await SaveAsync(customer, isNew: true);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return c is null ? NotFound() : View("Form", c);
    }

    [HttpPost, ActionName("Edit")]
    public async Task<IActionResult> EditPost(int id)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return c is null ? NotFound() : await SaveAsync(c, isNew: false);
    }

    private async Task<IActionResult> SaveAsync(Customer c, bool isNew)
    {
        await TryUpdateModelAsync(c, "", x => x.Document, x => x.Name, x => x.IeIndicator, x => x.StateRegistration,
            x => x.MunicipalRegistration, x => x.Email, x => x.Phone, x => x.Address);
        AddErrors(CatalogValidator.Customer(c));
        if (await db.Customers.AnyAsync(x => x.CompanyId == CompanyId && x.Document == c.Document && x.Id != c.Id))
            ModelState.AddModelError(nameof(c.Document), "Já existe um cadastro com este CPF/CNPJ.");
        if (!ModelState.IsValid) return View("Form", c);
        if (isNew) db.Customers.Add(c);
        await db.SaveChangesAsync();
        Audit.Log(db, DateTime.Now, CompanyId, UserName, isNew ? "Cadastrar cliente" : "Alterar cliente", "Cliente", c.Id, c.Document);
        await db.SaveChangesAsync();
        Success("Cliente salvo. Notas já emitidas não são alteradas.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (c is null) return NotFound();
        c.IsActive = !c.IsActive;
        Audit.Log(db, DateTime.Now, CompanyId, UserName, c.IsActive ? "Reativar cliente" : "Inativar cliente", "Cliente", id);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

[RequireCompany]
public class ProductsController(AppDbContext db) : AppController
{
    private IQueryable<Product> Query(string? q, bool inactive)
    {
        var query = db.Products.AsNoTracking().Where(p => p.CompanyId == CompanyId && (inactive || p.IsActive));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => EF.Functions.Like(p.Description, $"%{q}%") || p.Code.Contains(q) || (p.Gtin != null && p.Gtin.Contains(q)));
        return query.OrderBy(p => p.Description);
    }

    public async Task<IActionResult> Index(string? q, bool inactive = false, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Inactive = inactive;
        return View(await Query(q, inactive).ToPagedAsync(page, 20));
    }

    public async Task<IActionResult> Export(string? q, bool inactive = false)
    {
        var rows = (await Query(q, inactive).ToListAsync()).Select(p => new object?[]
        {
            p.Code, p.Description, p.Unit, p.Ncm, p.Cest, p.Origin, p.Gtin, p.Price, p.Cfop, p.IcmsTaxCode, p.IcmsRate,
            p.IsActive ? "Ativo" : "Inativo",
        });
        return Csv("produtos.csv", rows.Prepend(["Código", "Descrição", "Unidade", "NCM", "CEST", "Origem", "GTIN", "Preço", "CFOP",
            "CST/CSOSN", "Alíq. ICMS", "Situação"]));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new Product());

    [HttpPost, ActionName("Create")]
    public Task<IActionResult> CreatePost() => SaveAsync(new Product { CompanyId = CompanyId }, isNew: true);

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return p is null ? NotFound() : View("Form", p);
    }

    [HttpPost, ActionName("Edit")]
    public async Task<IActionResult> EditPost(int id)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return p is null ? NotFound() : await SaveAsync(p, isNew: false);
    }

    private async Task<IActionResult> SaveAsync(Product p, bool isNew)
    {
        await TryUpdateModelAsync(p, "", x => x.Code, x => x.Description, x => x.Unit, x => x.Ncm, x => x.Cest, x => x.Origin,
            x => x.Gtin, x => x.Price, x => x.Cfop, x => x.IcmsTaxCode, x => x.IcmsRate);
        AddErrors(CatalogValidator.Product(p));
        if (await db.Products.AnyAsync(x => x.CompanyId == CompanyId && x.Code == p.Code && x.Id != p.Id))
            ModelState.AddModelError(nameof(p.Code), "Já existe um produto com este código.");
        if (!ModelState.IsValid) return View("Form", p);
        if (isNew) db.Products.Add(p);
        await db.SaveChangesAsync();
        Audit.Log(db, DateTime.Now, CompanyId, UserName, isNew ? "Cadastrar produto" : "Alterar produto", "Produto", p.Id, p.Code);
        await db.SaveChangesAsync();
        Success("Produto salvo. Notas já emitidas não são alteradas.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (p is null) return NotFound();
        p.IsActive = !p.IsActive;
        Audit.Log(db, DateTime.Now, CompanyId, UserName, p.IsActive ? "Reativar produto" : "Inativar produto", "Produto", id);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

[RequireCompany]
public class ServicesController(AppDbContext db) : AppController
{
    private IQueryable<Service> Query(string? q, bool inactive)
    {
        var query = db.Services.AsNoTracking().Where(s => s.CompanyId == CompanyId && (inactive || s.IsActive));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(s => EF.Functions.Like(s.Description, $"%{q}%") || s.Code.Contains(q));
        return query.OrderBy(s => s.Description);
    }

    public async Task<IActionResult> Index(string? q, bool inactive = false, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Inactive = inactive;
        return View(await Query(q, inactive).ToPagedAsync(page, 20));
    }

    public async Task<IActionResult> Export(string? q, bool inactive = false)
    {
        var rows = (await Query(q, inactive).ToListAsync()).Select(s => new object?[]
        {
            s.Code, s.Description, s.NationalTaxCode, s.MunicipalTaxCode, s.NbsCode, s.IncidenceMunicipalityIbge,
            s.Price, s.IssRate, s.IssWithheld ? "Sim" : "Não", s.IsActive ? "Ativo" : "Inativo",
        });
        return Csv("servicos.csv", rows.Prepend(["Código", "Descrição", "Cód. trib. nacional", "Cód. municipal", "NBS",
            "Município incidência", "Preço", "Alíq. ISS", "ISS retido", "Situação"]));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new Service { IncidenceMunicipalityIbge = Company.Address.CityIbgeCode });

    [HttpPost, ActionName("Create")]
    public Task<IActionResult> CreatePost() => SaveAsync(new Service { CompanyId = CompanyId }, isNew: true);

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return s is null ? NotFound() : View("Form", s);
    }

    [HttpPost, ActionName("Edit")]
    public async Task<IActionResult> EditPost(int id)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return s is null ? NotFound() : await SaveAsync(s, isNew: false);
    }

    private async Task<IActionResult> SaveAsync(Service s, bool isNew)
    {
        await TryUpdateModelAsync(s, "", x => x.Code, x => x.Description, x => x.NationalTaxCode, x => x.MunicipalTaxCode,
            x => x.NbsCode, x => x.IncidenceMunicipalityIbge, x => x.Price, x => x.IssRate, x => x.IssWithheld);
        AddErrors(CatalogValidator.Service(s));
        if (await db.Services.AnyAsync(x => x.CompanyId == CompanyId && x.Code == s.Code && x.Id != s.Id))
            ModelState.AddModelError(nameof(s.Code), "Já existe um serviço com este código.");
        if (!ModelState.IsValid) return View("Form", s);
        if (isNew) db.Services.Add(s);
        await db.SaveChangesAsync();
        Audit.Log(db, DateTime.Now, CompanyId, UserName, isNew ? "Cadastrar serviço" : "Alterar serviço", "Serviço", s.Id, s.Code);
        await db.SaveChangesAsync();
        Success("Serviço salvo. Notas já emitidas não são alteradas.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (s is null) return NotFound();
        s.IsActive = !s.IsActive;
        Audit.Log(db, DateTime.Now, CompanyId, UserName, s.IsActive ? "Reativar serviço" : "Inativar serviço", "Serviço", id);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
