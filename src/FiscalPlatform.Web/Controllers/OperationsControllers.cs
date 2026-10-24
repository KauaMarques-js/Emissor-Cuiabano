using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Controllers;

public class AttemptFilter
{
    public FiscalDocumentType? Type { get; set; }
    public TransmissionOutcome? Outcome { get; set; }
    public string? Code { get; set; }
    public string? Customer { get; set; }
    public int? DocumentId { get; set; }
    public int? BatchId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>Central de rejeições e falhas: toda tentativa que não resultou em autorização/evento registrado.</summary>
[RequireCompany]
public class RejectionsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index(AttemptFilter f)
    {
        var q = db.Attempts.AsNoTracking().Include(a => a.FiscalDocument).ThenInclude(d => d!.Customer)
            .Where(a => a.CompanyId == CompanyId && a.Outcome != TransmissionOutcome.Autorizada && a.Outcome != TransmissionOutcome.EventoRegistrado);
        if (f.Type is not null) q = q.Where(a => a.DocumentType == f.Type);
        if (f.Outcome is not null) q = q.Where(a => a.Outcome == f.Outcome);
        if (!string.IsNullOrWhiteSpace(f.Code)) q = q.Where(a => a.Code == f.Code.Trim());
        if (f.DocumentId is not null) q = q.Where(a => a.FiscalDocumentId == f.DocumentId);
        if (f.BatchId is not null) q = q.Where(a => a.BatchId == f.BatchId);
        if (f.From is not null) q = q.Where(a => a.StartedAt >= f.From.Value.ToDateTime(TimeOnly.MinValue));
        if (f.To is not null) q = q.Where(a => a.StartedAt < f.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(f.Customer))
            q = q.Where(a => a.FiscalDocument!.Customer != null && EF.Functions.Like(a.FiscalDocument.Customer.Name, $"%{f.Customer}%"));
        ViewBag.Filter = f;
        return View(await q.OrderByDescending(a => a.Id).ToPagedAsync(f.Page, 25));
    }

    public async Task<IActionResult> Details(int id)
    {
        var attempt = await db.Attempts.AsNoTracking().Include(a => a.FiscalDocument)
            .FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == CompanyId);
        if (attempt is null) return NotFound();
        ViewBag.History = await db.Attempts.AsNoTracking()
            .Where(a => a.FiscalDocumentId == attempt.FiscalDocumentId && a.CompanyId == CompanyId)
            .OrderByDescending(a => a.Id).ToListAsync();
        return View(attempt);
    }
}

public class BatchSummary
{
    public required EmissionBatch Batch { get; init; }
    public required Dictionary<BatchItemStatus, int> Counts { get; init; }
}

[RequireCompany]
public class BatchesController(AppDbContext db, BatchService batches, EmissionService emission) : AppController
{
    public async Task<IActionResult> Index()
    {
        var list = await db.Batches.AsNoTracking().Include(b => b.Items)
            .Where(b => b.CompanyId == CompanyId).OrderByDescending(b => b.Id).Take(100).ToListAsync();
        return View(list.Select(b => new BatchSummary
        {
            Batch = b,
            Counts = b.Items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count()),
        }).ToList());
    }

    /// <summary>Valida os documentos selecionados e mostra aptos/inaptos antes de confirmar.</summary>
    [HttpPost]
    public async Task<IActionResult> Prepare(int[] ids)
    {
        if (ids.Length == 0) { Failure("Selecione ao menos um documento."); return RedirectToAction("Index", "Documents"); }
        var candidates = await batches.PrepareAsync(CompanyId, ids);
        ViewBag.Company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == CompanyId);
        return View(candidates);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int[] ids)
    {
        var (batch, error) = await batches.CreateAsync(CompanyId, ids, UserName);
        if (batch is null) { Failure(error!); return RedirectToAction("Index", "Documents"); }
        Success($"Lote #{batch.Id} criado com {batch.Items.Count} documento(s). Acompanhe o progresso abaixo.");
        return RedirectToAction(nameof(Details), new { id = batch.Id });
    }

    public async Task<IActionResult> Details(int id, BatchItemStatus? status)
    {
        var batch = await db.Batches.AsNoTracking()
            .Include(b => b.Items).ThenInclude(i => i.FiscalDocument).ThenInclude(d => d!.Customer)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == CompanyId);
        if (batch is null) return NotFound();
        ViewBag.Status = status;
        return View(batch);
    }

    public async Task<IActionResult> Export(int id)
    {
        var batch = await db.Batches.AsNoTracking()
            .Include(b => b.Items).ThenInclude(i => i.FiscalDocument).ThenInclude(d => d!.Customer)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == CompanyId);
        if (batch is null) return NotFound();
        var rows = batch.Items.Select(i => new object?[]
        {
            batch.Id, i.FiscalDocumentId, i.FiscalDocument?.DisplayNumber, i.FiscalDocument?.Customer?.Name,
            i.FiscalDocument?.TotalAmount, Ui.Label(i.Status), i.Message, Ui.Date(i.ProcessedAt), i.FiscalDocument?.AccessKey,
        });
        return Csv($"lote-{id}.csv", rows.Prepend(["Lote", "Documento", "Número", "Cliente", "Total", "Resultado", "Mensagem", "Processado em", "Chave"]));
    }

    /// <summary>Consulta a situação dos itens com resultado desconhecido, sem reenviar.</summary>
    [HttpPost]
    public async Task<IActionResult> QueryPending(int id)
    {
        var batch = await db.Batches.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == CompanyId);
        if (batch is null) return NotFound();
        var pending = batch.Items.Where(i => i.Status is BatchItemStatus.PendenteConsulta or BatchItemStatus.FalhaComunicacao).ToList();
        foreach (var item in pending)
        {
            var result = await emission.QueryAsync(CompanyId, item.FiscalDocumentId, UserName);
            var docStatus = await db.Documents.Where(d => d.Id == item.FiscalDocumentId).Select(d => d.Status).FirstAsync();
            item.Status = docStatus switch
            {
                DocumentStatus.Autorizada => BatchItemStatus.Autorizada,
                DocumentStatus.Rejeitada => BatchItemStatus.Rejeitada,
                DocumentStatus.Rascunho => BatchItemStatus.NaoEnviado,
                DocumentStatus.FalhaComunicacao => BatchItemStatus.FalhaComunicacao,
                _ => BatchItemStatus.PendenteConsulta,
            };
            item.Message = result.Message;
            item.ProcessedAt = DateTime.Now;
        }
        await db.SaveChangesAsync();
        Success($"{pending.Count} documento(s) consultado(s).");
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class ReportRow
{
    public FiscalDocumentType Type { get; init; }
    public DocumentStatus Status { get; init; }
    public int Count { get; init; }
    public decimal Total { get; init; }
}

[RequireCompany]
public class ReportsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        from ??= new DateOnly(today.Year, today.Month, 1);
        to ??= today;
        var start = from.Value.ToDateTime(TimeOnly.MinValue);
        var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // SQLite não soma decimal no servidor: agrega em memória.
        var docs = await db.Documents.AsNoTracking()
            .Where(d => d.CompanyId == CompanyId && d.CreatedAt >= start && d.CreatedAt < end && !d.Archived)
            .Select(d => new { d.Type, d.Status, d.TotalAmount }).ToListAsync();
        ViewBag.Rows = docs.GroupBy(d => new { d.Type, d.Status })
            .Select(g => new ReportRow { Type = g.Key.Type, Status = g.Key.Status, Count = g.Count(), Total = g.Sum(x => x.TotalAmount) })
            .OrderBy(r => r.Type).ThenBy(r => r.Status).ToList();
        ViewBag.Failures = await db.Attempts.AsNoTracking()
            .Where(a => a.CompanyId == CompanyId && a.StartedAt >= start && a.StartedAt < end
                        && a.Outcome != TransmissionOutcome.Autorizada && a.Outcome != TransmissionOutcome.EventoRegistrado)
            .GroupBy(a => a.Outcome).Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Outcome, x => x.Count);
        ViewBag.From = from; ViewBag.To = to;
        return View();
    }
}

public class AuditController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index(string? q, int? companyId, int page = 1)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (companyId is not null) query = query.Where(a => a.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => EF.Functions.Like(a.Action, $"%{q}%") || EF.Functions.Like(a.UserName, $"%{q}%")
                                     || (a.Details != null && EF.Functions.Like(a.Details, $"%{q}%")) || a.EntityId == q);
        ViewBag.Q = q; ViewBag.CompanyId = companyId;
        ViewBag.Companies = await db.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.DisplayName);
        return View(await query.OrderByDescending(a => a.Id).ToPagedAsync(page, 50));
    }
}

public class BackupController(BackupService backups, DataPaths paths, AppDbContext db) : AppController
{
    public IActionResult Index()
    {
        ViewBag.DataDirectory = paths.Root;
        return View(backups.List().ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var name = await backups.CreateAsync();
        Audit.Log(db, DateTime.Now, null, UserName, "Gerar backup", "Backup", name);
        await db.SaveChangesAsync();
        Success($"Backup {name} gerado.");
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Download(string name)
    {
        var path = backups.PathFor(name);
        return path is null ? NotFound() : PhysicalFile(path, "application/zip", name);
    }
}
