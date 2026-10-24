using FiscalPlatform.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Application;

public static class Audit
{
    public static void Log(IAppDbContext db, DateTime now, int? companyId, string user, string action,
        string entityType, object? entityId, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId, CreatedAt = now, UserName = user, Action = action,
            EntityType = entityType, EntityId = entityId?.ToString(), Details = details,
        });

    public static void Event(IAppDbContext db, FiscalDocument doc, DateTime now, string type, string description, string? user) =>
        db.Events.Add(new FiscalEvent
        {
            CompanyId = doc.CompanyId, FiscalDocumentId = doc.Id, CreatedAt = now,
            Type = type, Description = description, UserName = user,
        });
}

/// <summary>Criação e edição de rascunhos.</summary>
public class DocumentService(IAppDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetLocalNow().DateTime;

    public Task<FiscalDocument?> LoadAsync(int companyId, int id) =>
        db.Documents.Include(d => d.Items).Include(d => d.Customer)
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

    public async Task<FiscalDocument> CreateDraftAsync(Company company, FiscalDocumentType type, string user)
    {
        var doc = new FiscalDocument
        {
            CompanyId = company.Id,
            Type = type,
            Status = DocumentStatus.Rascunho,
            Environment = company.EnvironmentFor(type),
            Provider = company.ProviderFor(type),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        if (type == FiscalDocumentType.Nfse)
        {
            doc.Nfse.CompetenceDate = DateOnly.FromDateTime(Now);
            doc.Nfse.IncidenceMunicipalityIbge = company.Address.CityIbgeCode;
        }
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        Audit.Event(db, doc, Now, "Criado", "Rascunho criado.", user);
        Audit.Log(db, Now, company.Id, user, "Criar rascunho", "Documento", doc.Id, type.ToString());
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<string?> AddProductAsync(FiscalDocument doc, int productId, decimal quantity, decimal? unitPrice, decimal discount)
    {
        if (doc.Type != FiscalDocumentType.Nfe) return "Produtos só podem ser incluídos em NF-e.";
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == productId && x.CompanyId == doc.CompanyId && x.IsActive);
        if (p is null) return "Produto não encontrado para esta empresa.";
        var item = new FiscalDocumentItem { LineNumber = doc.Items.Count + 1, Quantity = quantity, UnitPrice = unitPrice ?? p.Price, Discount = discount };
        CopyProduct(p, item);
        doc.Items.Add(item);
        Touch(doc);
        return null;
    }

    public async Task<string?> AddServiceAsync(FiscalDocument doc, int serviceId, decimal? amount, decimal discount, string? description)
    {
        if (doc.Type != FiscalDocumentType.Nfse) return "Serviços só podem ser incluídos em NFS-e.";
        if (doc.Items.Count > 0) return "A NFS-e aceita apenas um serviço. Remova o atual para trocar.";
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == serviceId && x.CompanyId == doc.CompanyId && x.IsActive);
        if (s is null) return "Serviço não encontrado para esta empresa.";
        var item = new FiscalDocumentItem { LineNumber = 1, Quantity = 1, UnitPrice = amount ?? s.Price, Discount = discount };
        CopyService(s, item);
        if (!string.IsNullOrWhiteSpace(description)) item.Description = description.Trim();
        doc.Items.Add(item);
        doc.Nfse.IssWithheld = s.IssWithheld;
        if (!string.IsNullOrEmpty(s.IncidenceMunicipalityIbge)) doc.Nfse.IncidenceMunicipalityIbge = s.IncidenceMunicipalityIbge;
        Touch(doc);
        return null;
    }

    public void RemoveItem(FiscalDocument doc, int itemId)
    {
        var item = doc.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        doc.Items.Remove(item);
        db.DocumentItems.Remove(item);
        Touch(doc);
    }

    public void Touch(FiscalDocument doc)
    {
        doc.Recalculate();
        doc.UpdatedAt = Now;
    }

    /// <summary>
    /// Cria um novo rascunho a partir de uma nota existente, sem copiar número, chave, protocolo ou estado fiscal.
    /// Itens são atualizados a partir do cadastro atual quando ainda existir.
    /// </summary>
    public async Task<FiscalDocument> DuplicateAsync(FiscalDocument source, string user, bool asSubstitute = false)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == source.CompanyId);
        var doc = new FiscalDocument
        {
            CompanyId = source.CompanyId,
            Type = source.Type,
            Status = DocumentStatus.Rascunho,
            Environment = company.EnvironmentFor(source.Type),
            Provider = company.ProviderFor(source.Type),
            CustomerId = source.CustomerId,
            AdditionalInfo = source.AdditionalInfo,
            Nfe = new NfeData { NatureOfOperation = source.Nfe.NatureOfOperation, Freight = source.Nfe.Freight },
            Nfse = new NfseData
            {
                CompetenceDate = DateOnly.FromDateTime(Now),
                IncidenceMunicipalityIbge = source.Nfse.IncidenceMunicipalityIbge,
                IssWithheld = source.Nfse.IssWithheld,
            },
            SourceDocumentId = source.Id,
            SubstitutesDocumentId = asSubstitute ? source.Id : null,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var productIds = source.Items.Where(i => i.ProductId != null).Select(i => i.ProductId!.Value).ToList();
        var serviceIds = source.Items.Where(i => i.ServiceId != null).Select(i => i.ServiceId!.Value).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id) && p.CompanyId == source.CompanyId && p.IsActive).ToDictionaryAsync(p => p.Id);
        var services = await db.Services.Where(s => serviceIds.Contains(s.Id) && s.CompanyId == source.CompanyId && s.IsActive).ToDictionaryAsync(s => s.Id);

        foreach (var old in source.Items.OrderBy(i => i.LineNumber))
        {
            var item = new FiscalDocumentItem
            {
                LineNumber = old.LineNumber, ProductId = old.ProductId, ServiceId = old.ServiceId,
                Code = old.Code, Description = old.Description, Unit = old.Unit, Ncm = old.Ncm, Cest = old.Cest,
                Cfop = old.Cfop, Origin = old.Origin, Gtin = old.Gtin, IcmsTaxCode = old.IcmsTaxCode,
NationalTaxCode = old.NationalTaxCode,
                MunicipalTaxCode = old.MunicipalTaxCode, NbsCode = old.NbsCode,
                Quantity = old.Quantity, UnitPrice = old.UnitPrice, Discount = old.Discount, TaxRate = old.TaxRate,
            };
            if (old.ProductId is { } pid && products.TryGetValue(pid, out var p)) CopyProduct(p, item);
            if (old.ServiceId is { } sid && services.TryGetValue(sid, out var s))
            {
                var description = item.Description;
                CopyService(s, item);
                item.Description = description; // discriminação é específica da nota
            }
            doc.Items.Add(item);
        }
        doc.Recalculate();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var what = asSubstitute ? "Substituta" : "Duplicada";
        Audit.Event(db, doc, Now, "Criado", $"{what} a partir da nota #{source.Id} ({source.DisplayNumber}).", user);
        Audit.Event(db, source, Now, asSubstitute ? "Substituição iniciada" : "Duplicada",
            $"Novo rascunho #{doc.Id} criado a partir desta nota.", user);
        Audit.Log(db, Now, doc.CompanyId, user, asSubstitute ? "Criar substituta" : "Duplicar", "Documento", doc.Id, $"Origem #{source.Id}");
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task DeleteAsync(FiscalDocument doc, string user)
    {
        if (!DocumentRules.CanDelete(doc)) throw new InvalidOperationException("Este documento não pode ser excluído.");
        db.Events.RemoveRange(db.Events.Where(e => e.FiscalDocumentId == doc.Id));
        db.Attempts.RemoveRange(db.Attempts.Where(e => e.FiscalDocumentId == doc.Id));
        db.Documents.Remove(doc);
        Audit.Log(db, Now, doc.CompanyId, user, "Excluir rascunho", "Documento", doc.Id);
        await db.SaveChangesAsync();
    }

    public async Task ArchiveAsync(FiscalDocument doc, string user)
    {
        if (!DocumentRules.CanArchive(doc)) throw new InvalidOperationException("Este documento não pode ser arquivado.");
        doc.Archived = true;
        Touch(doc);
        Audit.Event(db, doc, Now, "Arquivado", "Rascunho arquivado (número reservado permanece registrado).", user);
        Audit.Log(db, Now, doc.CompanyId, user, "Arquivar", "Documento", doc.Id);
        await db.SaveChangesAsync();
    }

    private static void CopyProduct(Product p, FiscalDocumentItem i)
    {
        i.ProductId = p.Id; i.Code = p.Code; i.Description = p.Description; i.Unit = p.Unit; i.Ncm = p.Ncm;
        i.Cest = p.Cest; i.Cfop = p.Cfop; i.Origin = p.Origin; i.Gtin = p.Gtin; i.IcmsTaxCode = p.IcmsTaxCode; i.TaxRate = p.IcmsRate;
    }

    private static void CopyService(Service s, FiscalDocumentItem i)
    {
        i.ServiceId = s.Id; i.Code = s.Code; i.Description = s.Description; i.Unit = "UN";
        i.NationalTaxCode = s.NationalTaxCode; i.MunicipalTaxCode = s.MunicipalTaxCode; i.NbsCode = s.NbsCode;
        i.TaxRate = s.IssRate;
    }
}
