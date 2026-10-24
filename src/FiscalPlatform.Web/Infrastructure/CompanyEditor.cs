using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Infrastructure;

public sealed record CompanySaveResult(IReadOnlyList<ValidationError> Errors, string? Notice)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>Salva empresa e certificado A1 juntos (cadastro inicial e edição), numa única transação.</summary>
public class CompanyEditor(AppDbContext db, ICertificateStore certificates)
{
    public async Task<CompanySaveResult> SaveAsync(Company company, CompanyInput input, string user)
    {
        var isNew = company.Id == 0;
        input.ApplyTo(company);

        var errors = CatalogValidator.Company(company);
        if (await db.Companies.AnyAsync(c => c.Cnpj == company.Cnpj && c.Id != company.Id))
            errors.Add(new(nameof(CompanyInput.Cnpj), "Este CNPJ já está cadastrado."));
        if (!input.IssuesNfse && !input.IssuesNfe)
            errors.Add(new(nameof(CompanyInput.IssuesNfse), "Escolha ao menos um tipo de nota."));

        var file = input.CertificateFile;
        if (file is not null && Path.GetExtension(file.FileName).ToLowerInvariant() is not (".pfx" or ".p12"))
            errors.Add(new(nameof(CompanyInput.CertificateFile), "O certificado deve ser um arquivo .pfx ou .p12."));
        var hasCertificate = !isNew && await db.Certificates.AnyAsync(c => c.CompanyId == company.Id && c.IsActive);
        if (input.IssuesNfse && input.NfseMode != EmissionMode.Simulacao && file is null && !hasCertificate)
            errors.Add(new(nameof(CompanyInput.CertificateFile), "Para emitir de verdade, envie o certificado digital A1 (ou escolha Simulação por enquanto)."));
        if (errors.Count > 0) return new(errors, null);

        var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            var now = DateTime.Now;
            company.UpdatedAt = now;
            if (isNew)
            {
                company.CreatedAt = now;
                db.Companies.Add(company);
            }
            await db.SaveChangesAsync();
            Audit.Log(db, now, company.Id, user, isNew ? "Cadastrar empresa" : "Alterar empresa", "Empresa", company.Id, $"{company.Cnpj}, regime {company.TaxRegime}");
            await db.SaveChangesAsync();

            string? notice = null;
            if (file is not null)
            {
                await using var stream = file.OpenReadStream();
                var imported = await certificates.ImportAsync(company.Id, stream, input.CertificatePassword ?? "", user);
                if (!imported.Success)
                {
                    if (ownTransaction is not null) await ownTransaction.RollbackAsync();
                    if (isNew) db.ChangeTracker.Clear();
                    company.Id = isNew ? 0 : company.Id;
                    return new([new(nameof(CompanyInput.CertificateFile), imported.Message)], null);
                }
                notice = imported.Message;
            }
            if (ownTransaction is not null) await ownTransaction.CommitAsync();
            return new([], notice);
        }
        finally
        {
            if (ownTransaction is not null) await ownTransaction.DisposeAsync();
        }
    }
}
