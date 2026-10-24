using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.UnitTests;

/// <summary>Fluxo de emissão com SQLite em memória e o provedor de simulação.</summary>
public sealed class EmissionServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly AppDbContext db;
    private readonly EmissionService emission;
    private readonly DocumentService documents;
    private readonly Company company;
    private readonly Customer customer;

    public EmissionServiceTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        company = TestData.Company();
        company.Id = 0;
        db.Companies.Add(company);
        db.SaveChanges();
        customer = TestData.Customer(company.Id);
        customer.Id = 0;
        db.Customers.Add(customer);
        db.SaveChanges();

        var options = new FiscalOptions { TransmissionTimeoutSeconds = 5 };
        var resolver = new FiscalProviderResolver([new SimulatedFiscalProvider(FiscalDocumentType.Nfe), new SimulatedFiscalProvider(FiscalDocumentType.Nfse)]);
        emission = new EmissionService(db, resolver, new MemoryFileStore(), options, TimeProvider.System);
        documents = new DocumentService(db, TimeProvider.System);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private async Task<FiscalDocument> CreateNfeAsync(string? info = null)
    {
        var doc = TestData.NfeDraft(company.Id, customer.Id);
        doc.Status = DocumentStatus.Rascunho;
        doc.AdditionalInfo = info;
        doc.CreatedAt = doc.UpdatedAt = DateTime.Now;
        doc.Recalculate();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    [Fact]
    public async Task Authorizes_and_reserves_number()
    {
        var doc = await CreateNfeAsync();
        var result = await emission.EmitAsync(company.Id, doc.Id, "teste");

        Assert.True(result.Success, result.Message);
        Assert.Equal(DocumentStatus.Autorizada, doc.Status);
        Assert.Equal(1, doc.Number);
        Assert.Equal(44, doc.AccessKey!.Length);
        Assert.NotNull(doc.IssuerSnapshotJson);
        Assert.NotNull(doc.XmlPath);
        Assert.Equal(2, await db.Companies.Where(c => c.Id == company.Id).Select(c => c.NfeNextNumber).FirstAsync());
        Assert.Single(db.Attempts.Where(a => a.FiscalDocumentId == doc.Id));
    }

    [Fact]
    public async Task Rejection_keeps_number_and_message_then_retry_after_correction()
    {
        var doc = await CreateNfeAsync("[SIM:REJEITAR]");
        var first = await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.False(first.Success);
        Assert.Equal(DocumentStatus.Rejeitada, doc.Status);
        Assert.Equal("SIM-999", doc.LastErrorCode);
        var number = doc.Number;

        doc.AdditionalInfo = null;
        await db.SaveChangesAsync();
        var second = await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.True(second.Success);
        Assert.Equal(number, doc.Number);

        var attempts = await db.Attempts.Where(a => a.FiscalDocumentId == doc.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([TransmissionOutcome.Rejeitada, TransmissionOutcome.Autorizada], attempts.Select(a => a.Outcome));
        Assert.Contains("[SIM:REJEITAR]", attempts[0].Message);
    }

    [Fact]
    public async Task Timeout_blocks_resend_until_query()
    {
        var doc = await CreateNfeAsync("[SIM:TIMEOUT]");
        await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.Equal(DocumentStatus.FalhaComunicacao, doc.Status);

        var retry = await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.False(retry.Success);
        Assert.Contains("Consulte a situação", retry.Message);

        await emission.QueryAsync(company.Id, doc.Id, "teste");
        Assert.Equal(DocumentStatus.Autorizada, doc.Status);
    }

    [Fact]
    public async Task Query_not_found_releases_document_for_new_attempt()
    {
        var doc = await CreateNfeAsync();
        doc.Status = DocumentStatus.FalhaComunicacao;
        doc.Number = 7;
        await db.SaveChangesAsync();

        await emission.QueryAsync(company.Id, doc.Id, "teste");
        Assert.Equal(DocumentStatus.Rascunho, doc.Status);
        Assert.Equal(7, doc.Number);
    }

    [Fact]
    public async Task Validation_failure_does_not_transmit_or_reserve_number()
    {
        var doc = await CreateNfeAsync();
        doc.Items[0].Ncm = "123";
        await db.SaveChangesAsync();

        var result = await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(doc.Number);
        Assert.Equal(DocumentStatus.Rascunho, doc.Status);
        Assert.Equal(TransmissionOutcome.ValidacaoLocal, (await db.Attempts.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Document_of_other_company_is_not_found()
    {
        var doc = await CreateNfeAsync();
        var result = await emission.EmitAsync(company.Id + 99, doc.Id, "teste");
        Assert.False(result.Success);
        Assert.Contains("não encontrado", result.Message);
    }

    [Fact]
    public async Task Cancel_requires_reason_and_authorized_document()
    {
        var doc = await CreateNfeAsync();
        Assert.False((await emission.CancelAsync(company.Id, doc.Id, "motivo qualquer longo", "teste")).Success);

        await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.False((await emission.CancelAsync(company.Id, doc.Id, "curto", "teste")).Success);
        var ok = await emission.CancelAsync(company.Id, doc.Id, "Erro na quantidade vendida ao cliente", "teste");
        Assert.True(ok.Success);
        Assert.Equal(DocumentStatus.Cancelada, doc.Status);
    }

    [Fact]
    public async Task Duplicate_creates_clean_draft()
    {
        var doc = await CreateNfeAsync();
        await emission.EmitAsync(company.Id, doc.Id, "teste");
        var copy = await documents.DuplicateAsync(doc, "teste");

        Assert.Equal(DocumentStatus.Rascunho, copy.Status);
        Assert.Null(copy.Number);
        Assert.Null(copy.AccessKey);
        Assert.Null(copy.Protocol);
        Assert.NotEqual(doc.IdempotencyKey, copy.IdempotencyKey);
        Assert.Equal(doc.TotalAmount, copy.TotalAmount);
    }

    [Fact]
    public async Task Nfse_substitution_cancels_original_when_authorized()
    {
        var doc = TestData.NfseDraft(company.Id, customer.Id);
        doc.Status = DocumentStatus.Rascunho;
        doc.Recalculate();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        await emission.EmitAsync(company.Id, doc.Id, "teste");
        Assert.Equal(DocumentStatus.Autorizada, doc.Status);

        var substitute = await documents.DuplicateAsync(doc, "teste", asSubstitute: true);
        substitute.Nfse.CompetenceDate = doc.Nfse.CompetenceDate;
        await db.SaveChangesAsync();
        var result = await emission.EmitAsync(company.Id, substitute.Id, "teste");
        Assert.True(result.Success, result.Message);
        Assert.Equal(DocumentStatus.Cancelada, doc.Status);
        Assert.Equal(substitute.Id, doc.SubstitutedByDocumentId);
    }

    private sealed class MemoryFileStore : IFiscalFileStore
    {
        private readonly Dictionary<string, string> files = new();

        public Task<string> SaveAsync(int companyId, int documentId, string fileName, string content, CancellationToken ct = default)
        {
            var path = $"{companyId}/{documentId}/{fileName}";
            files[path] = content;
            return Task.FromResult(path);
        }

        public Stream? Open(string relativePath) =>
            files.TryGetValue(relativePath, out var c) ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(c)) : null;
    }
}
