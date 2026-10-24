using System.Security.Cryptography.X509Certificates;
using FiscalPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FiscalPlatform.Application;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Company> Companies { get; }
    DbSet<CertificateMetadata> Certificates { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<Service> Services { get; }
    DbSet<FiscalDocument> Documents { get; }
    DbSet<FiscalDocumentItem> DocumentItems { get; }
    DbSet<FiscalEvent> Events { get; }
    DbSet<TransmissionAttempt> Attempts { get; }
    DbSet<EmissionBatch> Batches { get; }
    DbSet<BatchItem> BatchItems { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public sealed class FiscalOptions
{
    public int TransmissionTimeoutSeconds { get; set; } = 60;
    /// <summary>Prazo padrão de cancelamento de NF-e; confirmar regra vigente na SEFAZ/MT.</summary>
    public int NfeCancelDeadlineHours { get; set; } = 24;
    public int CertificateWarningDays { get; set; } = 30;
    public int BatchConcurrency { get; set; } = 2;
}

/// <summary>Dados enviados ao adaptador de integração.</summary>
public sealed record ProviderRequest(
    FiscalDocument Document,
    Company Company,
    TransmissionOperation Operation,
    Guid OperationId,
    string? EventText = null,
    int EventSequence = 1)
{
    /// <summary>Código do motivo do evento (ex.: cancelamento de NFS-e: 1, 2 ou 9).</summary>
    public string? EventCode { get; init; }
    /// <summary>Chave de acesso da nota substituída, quando o documento é uma substituição.</summary>
    public string? SubstitutedAccessKey { get; init; }
}

public sealed record ProviderResult
{
    public TransmissionOutcome Outcome { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
    public string? Details { get; init; }
    public string? Protocol { get; init; }
    public string? AccessKey { get; init; }
    public string? OfficialNumber { get; init; }
    public string? VerificationCode { get; init; }
    public DateTime? AuthorizedAt { get; init; }
    public string? RequestPayload { get; init; }
    public string? ResponsePayload { get; init; }
    /// <summary>XML oficial retornado (autorizado/evento), para armazenamento.</summary>
    public string? XmlContent { get; init; }
}

/// <summary>
/// Contrato interno de integração fiscal. Não representa nomes de endpoints oficiais:
/// cada adaptador implementa conforme o manual vigente (SEFAZ/MT ou NFS-e Nacional).
/// </summary>
public interface IFiscalProvider
{
    FiscalDocumentType DocumentType { get; }
    ProviderKind Kind { get; }
    Task<ProviderResult> TransmitAsync(ProviderRequest request, CancellationToken ct);
    Task<ProviderResult> QueryAsync(ProviderRequest request, CancellationToken ct);
    Task<ProviderResult> CancelAsync(ProviderRequest request, CancellationToken ct);
    Task<ProviderResult> SendCorrectionLetterAsync(ProviderRequest request, CancellationToken ct);
}

public interface IFiscalProviderResolver
{
    IFiscalProvider Get(FiscalDocumentType type, ProviderKind kind);
}

public sealed class FiscalProviderResolver(IEnumerable<IFiscalProvider> providers) : IFiscalProviderResolver
{
    public IFiscalProvider Get(FiscalDocumentType type, ProviderKind kind) =>
        providers.FirstOrDefault(p => p.DocumentType == type && p.Kind == kind)
        ?? throw new InvalidOperationException($"Nenhum provedor registrado para {type}/{kind}.");
}

public sealed record CertificateImportResult(bool Success, string Message, CertificateMetadata? Certificate);

public interface ICertificateStore
{
    Task<CertificateImportResult> ImportAsync(int companyId, Stream pfx, string password, string user, CancellationToken ct = default);
    /// <summary>Carrega o certificado ativo da empresa para assinatura/mTLS. Nunca expor fora do servidor.</summary>
    Task<X509Certificate2?> LoadActiveAsync(int companyId, CancellationToken ct = default);
}

public interface IFiscalFileStore
{
    Task<string> SaveAsync(int companyId, int documentId, string fileName, string content, CancellationToken ct = default);
    Stream? Open(string relativePath);
}

public interface IDocumentPdfRenderer
{
    byte[] Render(FiscalDocument document, Company company);
}

public interface IBatchQueue
{
    void Enqueue(int batchId);
}
