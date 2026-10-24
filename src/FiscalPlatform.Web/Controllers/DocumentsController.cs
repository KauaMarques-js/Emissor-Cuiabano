using System.Globalization;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using FiscalPlatform.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Controllers;

/// <summary>Notas: emitir (New), ver resultado e agir (Show), listar (Index).</summary>
[RequireCompany]
public class DocumentsController(AppDbContext db, DocumentService documents, EmissionService emission,
    IFiscalFileStore files, IDocumentPdfRenderer pdf, FiscalOptions options) : AppController
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly DocumentStatus[] NeedsAttention =
        [DocumentStatus.Rejeitada, DocumentStatus.Pendente, DocumentStatus.Enviando, DocumentStatus.FalhaComunicacao];

    // ---------- Lista ----------

    private IQueryable<FiscalDocument> Query(DocumentFilter f)
    {
        var q = db.Documents.AsNoTracking().Include(d => d.Customer)
            .Where(d => d.CompanyId == CompanyId && d.Archived == f.Archived);
        if (f.Attention) q = q.Where(d => NeedsAttention.Contains(d.Status));
        if (f.Type is not null) q = q.Where(d => d.Type == f.Type);
        if (f.Status is not null) q = q.Where(d => d.Status == f.Status);
        if (f.Environment is not null) q = q.Where(d => d.Environment == f.Environment);
        if (f.BatchId is not null) q = q.Where(d => d.LastBatchId == f.BatchId);
        if (f.From is not null) q = q.Where(d => d.CreatedAt >= f.From.Value.ToDateTime(TimeOnly.MinValue));
        if (f.To is not null) q = q.Where(d => d.CreatedAt < f.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var text = f.Q.Trim();
            var digits = DocumentNumbers.Digits(text);
            var number = int.TryParse(text, out var n) ? n : (int?)null;
            q = q.Where(d => d.Number == number || d.Id == number || d.AccessKey == text
                             || (d.Customer != null && (EF.Functions.Like(d.Customer.Name, $"%{text}%")
                                                        || (digits.Length > 3 && d.Customer.Document.Contains(digits)))));
        }
        return q.OrderByDescending(d => d.Id);
    }

    public async Task<IActionResult> Index(DocumentFilter filter)
    {
        ViewBag.Filter = filter;
        ViewBag.AttentionCount = await db.Documents.CountAsync(d => d.CompanyId == CompanyId && !d.Archived && NeedsAttention.Contains(d.Status));
        return View(await Query(filter).ToPagedAsync(filter.Page, 25));
    }

    public async Task<IActionResult> Export(DocumentFilter filter)
    {
        var rows = (await Query(filter).ToListAsync()).Select(d => new object?[]
        {
            d.Id, Ui.TypeLabel(d.Type), d.Series, d.Number, Ui.Label(d.Status), Ui.Label(d.Environment),
            d.IsSimulated ? "Sim" : "Não", d.Customer?.Name, Ui.Doc(d.Customer?.Document), d.TotalAmount,
            Ui.Date(d.CreatedAt), Ui.Date(d.AuthorizedAt), d.AccessKey, d.LastErrorCode, d.LastErrorMessage,
        });
        return Csv("notas.csv", rows.Prepend(["ID", "Tipo", "Série", "Número", "Situação", "Ambiente", "Simulação", "Cliente", "CPF/CNPJ",
            "Total", "Criada em", "Autorizada em", "Chave", "Último código", "Última mensagem"]));
    }

    // ---------- Emitir ----------

    [HttpGet]
    public async Task<IActionResult> New(FiscalDocumentType? type, int? id)
    {
        if (id is null)
        {
            var input = new NoteInput
            {
                Type = type ?? (Company.NfseEnabled || !Company.NfeEnabled ? FiscalDocumentType.Nfse : FiscalDocumentType.Nfe),
                CompetenceDate = DateOnly.FromDateTime(DateTime.Now),
                NewCustomer = new Customer { Address = new Address { State = Company.Address.State } },
            };
            return View(await FormAsync(input, []));
        }
        var doc = await documents.LoadAsync(CompanyId, id.Value);
        if (doc is null) return NotFound();
        return DocumentRules.CanEdit(doc) ? View(await FormAsync(FromDocument(doc), [], doc)) : RedirectToAction(nameof(Show), new { id });
    }

    [HttpPost, ActionName("New")]
    public async Task<IActionResult> Save(NoteInput input, string? submit)
    {
        ModelState.Clear();
        var errors = new List<ValidationError>();
        FiscalDocument? doc = null;
        if (input.DocumentId is not null)
        {
            doc = await documents.LoadAsync(CompanyId, input.DocumentId.Value);
            if (doc is null || !DocumentRules.CanEdit(doc)) return RedirectToAction(nameof(Show), new { id = input.DocumentId });
            input.Type = doc.Type;
        }

        var customerId = await ResolveCustomerAsync(input, errors);
        var lines = ParseLines(input, errors);
        if (errors.Count > 0) return View("New", await FormAsync(input, errors, doc));

        if (doc is null)
        {
            doc = await documents.CreateDraftAsync(Company, input.Type, UserName);
            input.DocumentId = doc.Id;
        }
        foreach (var item in doc.Items.ToList()) documents.RemoveItem(doc, item.Id);
        doc.CustomerId = customerId;
        doc.AdditionalInfo = input.AdditionalInfo;
        foreach (var line in lines)
        {
            var error = doc.Type == FiscalDocumentType.Nfe
                ? await documents.AddProductAsync(doc, line.ItemId, line.Quantity, line.Price, line.Discount)
                : await documents.AddServiceAsync(doc, line.ItemId, line.Price, line.Discount, input.ServiceDescription);
            if (error is not null) errors.Add(new("Itens", error));
        }
        if (doc.Type == FiscalDocumentType.Nfe)
        {
            doc.Nfe.NatureOfOperation = input.NatureOfOperation;
            doc.Nfe.Freight = input.Freight;
        }
        else
        {
            doc.Nfse.CompetenceDate = input.CompetenceDate;
            doc.Nfse.IssWithheld = input.IssWithheld;
        }
        documents.Touch(doc);
        Audit.Event(db, doc, DateTime.Now, "Editado", "Dados da nota salvos.", UserName);
        await db.SaveChangesAsync();

        if (errors.Count > 0) return View("New", await FormAsync(input, errors, doc));
        if (submit == "draft")
        {
            Success("Rascunho salvo. Ele fica em Notas para você continuar depois.");
            return RedirectToAction(nameof(New), new { id = doc.Id });
        }

        var pending = await emission.ValidateAsync(CompanyId, doc.Id);
        if (pending.Count > 0) return View("New", await FormAsync(input, pending, doc));

        await emission.EmitAsync(CompanyId, doc.Id, UserName);
        return RedirectToAction(nameof(Show), new { id = doc.Id });
    }

    // ---------- Nota: resultado e ações ----------

    public async Task<IActionResult> Show(int id)
    {
        var doc = await documents.LoadAsync(CompanyId, id);
        if (doc is null) return NotFound();
        return View(new NotePageModel
        {
            Document = doc,
            Attempts = await db.Attempts.AsNoTracking().Where(a => a.FiscalDocumentId == id && a.CompanyId == CompanyId).OrderByDescending(a => a.Id).ToListAsync(),
            Events = await db.Events.AsNoTracking().Where(e => e.FiscalDocumentId == id && e.CompanyId == CompanyId).OrderByDescending(e => e.Id).ToListAsync(),
            Pending = DocumentRules.CanEdit(doc) ? await emission.ValidateAsync(CompanyId, id) : [],
            CanCancel = DocumentRules.CanCancel(doc, DateTime.Now, options.NfeCancelDeadlineHours),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Query(int id) => Report(await emission.QueryAsync(CompanyId, id, UserName), id);

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string? reason, string? reasonCode) =>
        Report(await emission.CancelAsync(CompanyId, id, reason ?? "", UserName, reasonCode: reasonCode), id);

    [HttpPost]
    public async Task<IActionResult> Correction(int id, string? text) =>
        Report(await emission.SendCorrectionLetterAsync(CompanyId, id, text ?? "", UserName), id);

    [HttpPost]
    public async Task<IActionResult> Duplicate(int id) => await CopyAsync(id, substitute: false);

    [HttpPost]
    public async Task<IActionResult> Substitute(int id) => await CopyAsync(id, substitute: true);

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await documents.LoadAsync(CompanyId, id);
        if (doc is null) return NotFound();
        if (!DocumentRules.CanDelete(doc)) return Report(OperationResult.Fail("Só é possível excluir rascunhos que nunca foram enviados."), id);
        await documents.DeleteAsync(doc, UserName);
        Success("Rascunho excluído.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Archive(int id)
    {
        var doc = await documents.LoadAsync(CompanyId, id);
        if (doc is null) return NotFound();
        if (!DocumentRules.CanArchive(doc)) return Report(OperationResult.Fail("Esta nota não pode ser arquivada."), id);
        await documents.ArchiveAsync(doc, UserName);
        Success("Nota arquivada.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Pdf(int id)
    {
        var doc = await documents.LoadAsync(CompanyId, id);
        if (doc is null) return NotFound();
        Audit.Event(db, doc, DateTime.Now, "Download", "PDF baixado.", UserName);
        await db.SaveChangesAsync();
        var name = $"{(doc.Type == FiscalDocumentType.Nfe ? "nfe" : "nfse")}-{doc.Series}-{doc.Number ?? doc.Id}{(doc.IsSimulated ? "-simulacao" : "")}.pdf";
        return File(pdf.Render(doc, Company), "application/pdf", name);
    }

    public async Task<IActionResult> Xml(int id)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == CompanyId);
        if (doc?.XmlPath is null || files.Open(doc.XmlPath) is not { } stream) return NotFound();
        Audit.Event(db, doc, DateTime.Now, "Download", "XML baixado.", UserName);
        await db.SaveChangesAsync();
        return File(stream, "application/xml", Path.GetFileName(doc.XmlPath));
    }

    private IActionResult Report(OperationResult result, int id)
    {
        if (result.Success) Success(result.Message);
        else Failure(result.Errors.Count > 0 ? result.Message + " " + string.Join(" | ", result.Errors) : result.Message);
        return RedirectToAction(nameof(Show), new { id });
    }

    private async Task<IActionResult> CopyAsync(int id, bool substitute)
    {
        var doc = await documents.LoadAsync(CompanyId, id);
        if (doc is null) return NotFound();
        if (substitute && !DocumentRules.CanSubstitute(doc)) return Report(OperationResult.Fail("Esta nota não pode ser substituída."), id);
        var copy = await documents.DuplicateAsync(doc, UserName, substitute);
        Success(substitute
            ? "Corrija os dados e emita: a nota nova substitui a anterior, que será cancelada automaticamente."
            : $"Cópia da nota #{id} criada. Confira os dados e emita.");
        return RedirectToAction(nameof(New), new { id = copy.Id });
    }

    // ---------- Apoio do formulário ----------

    private async Task<int?> ResolveCustomerAsync(NoteInput input, List<ValidationError> errors)
    {
        var n = input.NewCustomer;
        var wantsNew = !string.IsNullOrWhiteSpace(n.Document) || !string.IsNullOrWhiteSpace(n.Name);
        if (!wantsNew)
        {
            if (input.CustomerId is { } id && await db.Customers.AnyAsync(c => c.Id == id && c.CompanyId == CompanyId)) return id;
            errors.Add(new("Cliente", "Escolha um cliente ou cadastre um novo."));
            return null;
        }

        var customer = new Customer
        {
            CompanyId = CompanyId, CreatedAt = DateTime.Now, Document = n.Document ?? "", Name = n.Name?.Trim() ?? "",
            IeIndicator = n.IeIndicator is 1 or 2 ? n.IeIndicator : 9, StateRegistration = n.StateRegistration,
            Email = n.Email, Phone = n.Phone, Address = n.Address,
        };
        var customerErrors = CatalogValidator.Customer(customer); // também normaliza o CPF/CNPJ
        // Já cadastrado: reaproveita em vez de duplicar.
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Document == customer.Document);
        if (existing is null)
        {
            if (customerErrors.Count > 0)
            {
                errors.AddRange(customerErrors.Select(e => new ValidationError("Novo cliente", e.Message)));
                return null;
            }
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            Audit.Log(db, DateTime.Now, CompanyId, UserName, "Cadastrar cliente", "Cliente", customer.Id, customer.Document);
        }
        input.CustomerId = (existing ?? customer).Id;
        input.NewCustomer = new Customer();
        return input.CustomerId;
    }

    private sealed record Line(int ItemId, decimal Quantity, decimal? Price, decimal Discount);

    private static List<Line> ParseLines(NoteInput input, List<ValidationError> errors)
    {
        var lines = new List<Line>();
        for (var i = 0; i < input.LineItem.Count; i++)
        {
            string? At(List<string?> list) => i < list.Count ? list[i]?.Trim() : null;
            if (string.IsNullOrEmpty(At(input.LineItem))) continue;
            var label = input.Type == FiscalDocumentType.Nfe ? $"Produto {i + 1}" : "Serviço";
            if (!int.TryParse(At(input.LineItem), out var itemId)) { errors.Add(new(label, "Escolha um item da lista.")); continue; }
            if (!TryDecimal(At(input.LineQty), 1, out var qty) || qty <= 0) errors.Add(new(label, "Quantidade inválida."));
            decimal? price = null;
            if (At(input.LinePrice) is { Length: > 0 } priceText)
            {
                if (TryDecimal(priceText, 0, out var p) && p > 0) price = p;
                else errors.Add(new(label, "Valor inválido. Use vírgula nos centavos, ex.: 189,90."));
            }
            if (!TryDecimal(At(input.LineDiscount), 0, out var discount) || discount < 0) errors.Add(new(label, "Desconto inválido."));
            lines.Add(new Line(itemId, qty, price, discount));
        }
        if (lines.Count == 0)
            errors.Add(new("Itens", input.Type == FiscalDocumentType.Nfe ? "Adicione ao menos um produto." : "Escolha o serviço prestado."));
        return lines;
    }

    private static bool TryDecimal(string? text, decimal fallback, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text)) { value = fallback; return true; }
        return decimal.TryParse(text.Replace("R$", "").Trim(), NumberStyles.Number, PtBr, out value);
    }

    private static NoteInput FromDocument(FiscalDocument doc)
    {
        var items = doc.Items.OrderBy(i => i.LineNumber).ToList();
        return new NoteInput
        {
            DocumentId = doc.Id,
            Type = doc.Type,
            CustomerId = doc.CustomerId,
            LineItem = items.Select(i => (i.ProductId ?? i.ServiceId)?.ToString()).ToList(),
            LineQty = items.Select(i => (string?)i.Quantity.ToString("0.####", PtBr)).ToList(),
            LinePrice = items.Select(i => (string?)i.UnitPrice.ToString("0.00", PtBr)).ToList(),
            LineDiscount = items.Select(i => (string?)i.Discount.ToString("0.00", PtBr)).ToList(),
            ServiceDescription = doc.Type == FiscalDocumentType.Nfse ? items.FirstOrDefault()?.Description : null,
            CompetenceDate = doc.Nfse.CompetenceDate,
            IssWithheld = doc.Nfse.IssWithheld,
            NatureOfOperation = doc.Nfe.NatureOfOperation,
            Freight = doc.Nfe.Freight,
            AdditionalInfo = doc.AdditionalInfo,
        };
    }

    private async Task<NoteFormModel> FormAsync(NoteInput input, IReadOnlyList<ValidationError> errors, FiscalDocument? doc = null) => new()
    {
        Input = input,
        Company = Company,
        Errors = errors,
        Document = doc,
        Customers = await db.Customers.AsNoTracking().Where(c => c.CompanyId == CompanyId && (c.IsActive || c.Id == input.CustomerId)).OrderBy(c => c.Name).ToListAsync(),
        Products = await db.Products.AsNoTracking().Where(p => p.CompanyId == CompanyId && p.IsActive).OrderBy(p => p.Description).ToListAsync(),
        Services = await db.Services.AsNoTracking().Where(s => s.CompanyId == CompanyId && s.IsActive).OrderBy(s => s.Description).ToListAsync(),
    };
}
