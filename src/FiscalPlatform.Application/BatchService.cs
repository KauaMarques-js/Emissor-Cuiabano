using FiscalPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalPlatform.Application;

public sealed record BatchCandidate(FiscalDocument Document, List<ValidationError> Errors)
{
    public bool Eligible => Errors.Count == 0;
}

/// <summary>Monta lotes de emissão: mesma empresa, mesmo tipo, cada documento validado individualmente.</summary>
public class BatchService(IAppDbContext db, EmissionService emission, IBatchQueue queue, TimeProvider clock)
{
    private DateTime Now => clock.GetLocalNow().DateTime;

    public async Task<List<BatchCandidate>> PrepareAsync(int companyId, IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        var docs = await db.Documents.Include(d => d.Items).Include(d => d.Customer)
            .Where(d => d.CompanyId == companyId && idList.Contains(d.Id))
            .OrderBy(d => d.Id).ToListAsync();
        var type = docs.Select(d => d.Type).FirstOrDefault();

        var result = new List<BatchCandidate>();
        foreach (var doc in docs)
        {
            var errors = new List<ValidationError>();
            if (doc.Type != type) errors.Add(new("Tipo", "Lote não pode misturar NF-e e NFS-e."));
            else if (DocumentRules.MustQueryBeforeRetry(doc)) errors.Add(new("Situação", "Resultado anterior desconhecido: consulte a situação antes."));
            else if (!DocumentRules.CanEmit(doc)) errors.Add(new("Situação", $"Documento em estado '{doc.Status}' não pode ser emitido."));
            else errors.AddRange(await emission.ValidateAsync(companyId, doc.Id));
            result.Add(new BatchCandidate(doc, errors));
        }
        return result;
    }

    public async Task<(EmissionBatch? Batch, string? Error)> CreateAsync(int companyId, IEnumerable<int> ids, string user)
    {
        var candidates = await PrepareAsync(companyId, ids);
        var eligible = candidates.Where(c => c.Eligible).Select(c => c.Document).ToList();
        if (eligible.Count == 0) return (null, "Nenhum documento apto para emissão no lote.");

        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var type = eligible[0].Type;
        var batch = new EmissionBatch
        {
            CompanyId = companyId,
            DocumentType = type,
            Environment = company.EnvironmentFor(type),
            Provider = company.ProviderFor(type),
            Status = BatchStatus.Processando,
            CreatedAt = Now,
            CreatedBy = user,
            Items = eligible.Select(d => new BatchItem { FiscalDocumentId = d.Id, Status = BatchItemStatus.Aguardando }).ToList(),
        };
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        Audit.Log(db, Now, companyId, user, "Criar lote", "Lote", batch.Id,
            $"{type}, {batch.Environment}, {batch.Provider}, documentos: {string.Join(",", eligible.Select(d => d.Id))}");
        await db.SaveChangesAsync();
        queue.Enqueue(batch.Id);
        return (batch, null);
    }
}

/// <summary>Processa os itens de um lote em fila, com concorrência limitada, cada um em seu próprio escopo.</summary>
public class BatchProcessor(IServiceScopeFactory scopes, FiscalOptions options)
{
    public async Task ProcessAsync(int batchId, CancellationToken ct)
    {
        int companyId; string user; List<int> itemIds;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var batch = await db.Batches.Include(b => b.Items).ThenInclude(i => i.FiscalDocument)
                .FirstOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch is null || batch.Status == BatchStatus.Concluido) return;
            companyId = batch.CompanyId;
            user = batch.CreatedBy ?? "sistema";

            // Itens interrompidos (ex.: aplicação reiniciada no meio do envio) nunca são reenviados às cegas.
            foreach (var item in batch.Items.Where(i => i.Status == BatchItemStatus.Processando))
            {
                if (item.FiscalDocument is { } d && DocumentRules.CanEmit(d)) item.Status = BatchItemStatus.Aguardando;
                else
                {
                    item.Status = BatchItemStatus.PendenteConsulta;
                    item.Message = "Processamento interrompido. Consulte a situação do documento.";
                }
            }
            await db.SaveChangesAsync(ct);
            itemIds = batch.Items.Where(i => i.Status == BatchItemStatus.Aguardando).Select(i => i.Id).ToList();
        }

        await Parallel.ForEachAsync(itemIds,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.BatchConcurrency), CancellationToken = ct },
            async (itemId, token) =>
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var emission = scope.ServiceProvider.GetRequiredService<EmissionService>();
                var item = await db.BatchItems.FirstAsync(i => i.Id == itemId, token);
                item.Status = BatchItemStatus.Processando;
                await db.SaveChangesAsync(token);

                var result = await emission.EmitAsync(companyId, item.FiscalDocumentId, user, batchId, token);
                var status = await db.Documents.Where(d => d.Id == item.FiscalDocumentId).Select(d => d.Status).FirstAsync(token);
                item.Status = !result.Sent ? BatchItemStatus.NaoEnviado : status switch
                {
                    DocumentStatus.Autorizada => BatchItemStatus.Autorizada,
                    DocumentStatus.Rejeitada => BatchItemStatus.Rejeitada,
                    DocumentStatus.FalhaComunicacao => BatchItemStatus.FalhaComunicacao,
                    _ => BatchItemStatus.PendenteConsulta,
                };
                item.Message = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : result.Message;
                item.ProcessedAt = DateTime.Now;
                await db.SaveChangesAsync(token);
            });

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var batch = await db.Batches.FirstAsync(b => b.Id == batchId, ct);
            batch.Status = BatchStatus.Concluido;
            batch.FinishedAt = DateTime.Now;
            await db.SaveChangesAsync(ct);
        }
    }
}
