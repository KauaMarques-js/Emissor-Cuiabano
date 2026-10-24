using System.Text.Json;
using FiscalPlatform.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Application;

public sealed record OperationResult(bool Success, bool Sent, string Message, IReadOnlyList<ValidationError> Errors)
{
    public static OperationResult Fail(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(false, false, message, errors ?? []);
}

/// <summary>
/// Emissão, consulta, cancelamento e carta de correção. Toda chamada ao provedor gera um
/// <see cref="TransmissionAttempt"/>; timeouts nunca viram rejeição nem reenvio automático.
/// </summary>
public class EmissionService(IAppDbContext db, IFiscalProviderResolver providers, IFiscalFileStore files,
    FiscalOptions options, TimeProvider clock)
{
    private static readonly SemaphoreSlim NumberLock = new(1, 1);
    private DateTime Now => clock.GetLocalNow().DateTime;

    private Task<FiscalDocument?> LoadAsync(int companyId, int documentId) =>
        db.Documents.Include(d => d.Items).Include(d => d.Customer)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.CompanyId == companyId);

    private Task<CertificateMetadata?> ActiveCertificateAsync(int companyId) =>
        db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsActive);

    public async Task<List<ValidationError>> ValidateAsync(int companyId, int documentId)
    {
        var doc = await LoadAsync(companyId, documentId);
        if (doc is null) return [new("", "Documento não encontrado.")];
        return await ValidateAsync(doc);
    }

    private async Task<List<ValidationError>> ValidateAsync(FiscalDocument doc)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == doc.CompanyId);
        var customer = doc.CustomerId is null ? null
            : await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == doc.CustomerId);
        var cert = await ActiveCertificateAsync(doc.CompanyId);
        doc.Recalculate();
        return DocumentValidator.Validate(doc, company, customer, cert, Now, options.CertificateWarningDays);
    }

    public async Task<OperationResult> EmitAsync(int companyId, int documentId, string user, int? batchId = null, CancellationToken ct = default)
    {
        var doc = await LoadAsync(companyId, documentId);
        if (doc is null) return OperationResult.Fail("Documento não encontrado para a empresa selecionada.");
        if (DocumentRules.MustQueryBeforeRetry(doc))
            return OperationResult.Fail("O resultado da última transmissão é desconhecido. Consulte a situação antes de tentar novamente.");
        if (!DocumentRules.CanEmit(doc))
            return OperationResult.Fail($"Documento em estado '{doc.Status}' não pode ser emitido.");

        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var errors = await ValidateAsync(doc);
        if (errors.Count > 0)
        {
            var failed = NewAttempt(doc, TransmissionOperation.Emissao, user, batchId, company);
            failed.Outcome = TransmissionOutcome.ValidacaoLocal;
            failed.Message = string.Join(Environment.NewLine, errors);
            failed.FinishedAt = Now;
            failed.Summary = AttemptSummaries.For(failed.Outcome);
            db.Attempts.Add(failed);
            Audit.Event(db, doc, Now, "Validação", $"Emissão bloqueada: {errors.Count} pendência(s).", user);
            await db.SaveChangesAsync(ct);
            return OperationResult.Fail("Corrija as pendências antes de emitir.", errors);
        }

        // Trava o documento (Status é token de concorrência): impede envio duplicado simultâneo.
        var previousStatus = doc.Status;
        doc.Status = DocumentStatus.Enviando;
        doc.Environment = company.EnvironmentFor(doc.Type);
        doc.Provider = company.ProviderFor(doc.Type);
        doc.UpdatedAt = Now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail("Este documento já está sendo processado por outra operação.");
        }

        if (doc.Number is null)
        {
            doc.Series = doc.Type == FiscalDocumentType.Nfe ? company.NfeSeries : company.NfseSeries;
            doc.Number = await ReserveNumberAsync(company.Id, doc.Type, ct);
        }
        doc.IssuerSnapshotJson = IssuerSnapshot(company);
        doc.CustomerSnapshotJson = await CustomerSnapshotAsync(doc.CustomerId);
        Audit.Event(db, doc, Now, "Transmissão", $"Transmissão iniciada ({Label(doc)}).", user);
        await db.SaveChangesAsync(ct);

        var attempt = NewAttempt(doc, TransmissionOperation.Emissao, user, batchId, company);
        var provider = providers.Get(doc.Type, doc.Provider);
        var substitutedKey = await SubstitutedKeyAsync(doc, ct);
        var result = await CallAsync(() => provider.TransmitAsync(new ProviderRequest(doc, company, TransmissionOperation.Emissao, attempt.OperationId)
            {
                SubstitutedAccessKey = substitutedKey,
            }, ct), ct);
        Fill(attempt, result);
        db.Attempts.Add(attempt);

        var sent = true;
        switch (result.Outcome)
        {
            case TransmissionOutcome.Autorizada:
                await MarkAuthorizedAsync(doc, result, user, ct);
                break;
            case TransmissionOutcome.Rejeitada:
                MarkRejected(doc, result);
                break;
            case TransmissionOutcome.EmProcessamento:
                doc.Status = DocumentStatus.Pendente;
                break;
            case TransmissionOutcome.FalhaCertificado
                or TransmissionOutcome.Indisponivel or TransmissionOutcome.ValidacaoLocal:
                // Nada foi aceito pelo serviço: volta ao estado anterior, mantendo o número reservado.
                doc.Status = previousStatus;
                doc.LastErrorCode = result.Code;
                doc.LastErrorMessage = result.Message;
                sent = false;
                break;
            default:
                // Timeout / erro inesperado: resultado desconhecido, exige consulta antes de reenviar.
                doc.Status = DocumentStatus.FalhaComunicacao;
                doc.LastErrorCode = result.Code;
                doc.LastErrorMessage = result.Message;
                break;
        }
        doc.UpdatedAt = Now;
        if (batchId is not null) doc.LastBatchId = batchId;
        Audit.Event(db, doc, Now, "Resposta", $"{Label(result.Outcome)}{Suffix(result)}", user);
        Audit.Log(db, Now, doc.CompanyId, user, "Emitir", "Documento", doc.Id, $"{doc.Type} {doc.DisplayNumber}: {result.Outcome}");
        await db.SaveChangesAsync(ct);

        var ok = doc.Status == DocumentStatus.Autorizada;
        return new OperationResult(ok, sent, ok ? "Documento autorizado." : $"{Label(result.Outcome)}{Suffix(result)}", []);
    }

    public async Task<OperationResult> QueryAsync(int companyId, int documentId, string user, CancellationToken ct = default)
    {
        var doc = await LoadAsync(companyId, documentId);
        if (doc is null) return OperationResult.Fail("Documento não encontrado.");
        if (!DocumentRules.CanQuery(doc)) return OperationResult.Fail("Não há o que consultar para este documento.");

        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var attempt = NewAttempt(doc, TransmissionOperation.Consulta, user, null, company);
        var provider = providers.Get(doc.Type, doc.Provider);
        var result = await CallAsync(() => provider.QueryAsync(new ProviderRequest(doc, company, TransmissionOperation.Consulta, attempt.OperationId), ct), ct);
        Fill(attempt, result);
        db.Attempts.Add(attempt);

        var unknown = DocumentRules.MustQueryBeforeRetry(doc);
        var message = $"Consulta: {Label(result.Outcome)}{Suffix(result)}";
        switch (result.Outcome)
        {
            case TransmissionOutcome.Autorizada when doc.Status != DocumentStatus.Autorizada && doc.Status != DocumentStatus.Cancelada:
                await MarkAuthorizedAsync(doc, result, user, ct);
                break;
            case TransmissionOutcome.Rejeitada when unknown:
                MarkRejected(doc, result);
                break;
            case TransmissionOutcome.EmProcessamento when unknown:
                doc.Status = DocumentStatus.Pendente;
                break;
            case TransmissionOutcome.NaoEncontrada when unknown:
                doc.Status = DocumentStatus.Rascunho;
                message = "Consulta confirmou que a nota não foi processada pelo serviço. Liberada para nova tentativa com o mesmo número.";
                break;
        }
        doc.UpdatedAt = Now;
        Audit.Event(db, doc, Now, "Consulta", message, user);
        Audit.Log(db, Now, doc.CompanyId, user, "Consultar", "Documento", doc.Id, result.Outcome.ToString());
        await db.SaveChangesAsync(ct);
        return new OperationResult(result.Outcome != TransmissionOutcome.Timeout && result.Outcome != TransmissionOutcome.ErroInterno, true, message, []);
    }

    public async Task<OperationResult> CancelAsync(int companyId, int documentId, string reason, string user,
        CancellationToken ct = default, string? reasonCode = null)
    {
        var doc = await LoadAsync(companyId, documentId);
        if (doc is null) return OperationResult.Fail("Documento não encontrado.");
        if (!DocumentRules.CanCancel(doc, Now, options.NfeCancelDeadlineHours))
            return OperationResult.Fail("Cancelamento não permitido para o estado/prazo deste documento.");
        reason = (reason ?? "").Trim();
        if (reason.Length is < 15 or > 255)
            return OperationResult.Fail("A justificativa deve ter entre 15 e 255 caracteres.", [new("Justificativa", "Entre 15 e 255 caracteres.")]);

        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var attempt = NewAttempt(doc, TransmissionOperation.Cancelamento, user, null, company);
        var provider = providers.Get(doc.Type, doc.Provider);
        var result = await CallAsync(() => provider.CancelAsync(new ProviderRequest(doc, company, TransmissionOperation.Cancelamento, attempt.OperationId, reason)
            {
                EventCode = reasonCode,
            }, ct), ct);
        Fill(attempt, result);
        db.Attempts.Add(attempt);

        var ok = result.Outcome == TransmissionOutcome.EventoRegistrado;
        if (ok)
        {
            doc.Status = DocumentStatus.Cancelada;
            doc.CancelledAt = Now;
            doc.CancelReason = reason;
            doc.CancelProtocol = result.Protocol;
            if (!string.IsNullOrEmpty(result.XmlContent))
                await files.SaveAsync(doc.CompanyId, doc.Id, $"cancelamento-{doc.Number}.xml", result.XmlContent, ct);
        }
        doc.UpdatedAt = Now;
        Audit.Event(db, doc, Now, "Cancelamento", $"{Label(result.Outcome)}{Suffix(result)} Justificativa: {reason}", user);
        Audit.Log(db, Now, doc.CompanyId, user, "Cancelar", "Documento", doc.Id, result.Outcome.ToString());
        await db.SaveChangesAsync(ct);
        return new OperationResult(ok, true, ok ? "Cancelamento registrado." : $"Cancelamento não registrado: {Label(result.Outcome)}{Suffix(result)}", []);
    }

    public async Task<OperationResult> SendCorrectionLetterAsync(int companyId, int documentId, string text, string user, CancellationToken ct = default)
    {
        var doc = await LoadAsync(companyId, documentId);
        if (doc is null) return OperationResult.Fail("Documento não encontrado.");
        if (!DocumentRules.CanSendCorrectionLetter(doc)) return OperationResult.Fail("Carta de correção só se aplica a NF-e autorizada.");
        text = (text ?? "").Trim();
        if (text.Length is < 15 or > 1000)
            return OperationResult.Fail("A correção deve ter entre 15 e 1000 caracteres.", [new("Correção", "Entre 15 e 1000 caracteres.")]);
        if (doc.CorrectionLetterCount >= 20) return OperationResult.Fail("Limite de 20 cartas de correção atingido.");

        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var attempt = NewAttempt(doc, TransmissionOperation.CartaCorrecao, user, null, company);
        var seq = doc.CorrectionLetterCount + 1;
        var provider = providers.Get(doc.Type, doc.Provider);
        var result = await CallAsync(() => provider.SendCorrectionLetterAsync(new ProviderRequest(doc, company, TransmissionOperation.CartaCorrecao, attempt.OperationId, text, seq), ct), ct);
        Fill(attempt, result);
        db.Attempts.Add(attempt);

        var ok = result.Outcome == TransmissionOutcome.EventoRegistrado;
        if (ok)
        {
            doc.CorrectionLetterCount = seq;
            if (!string.IsNullOrEmpty(result.XmlContent))
                await files.SaveAsync(doc.CompanyId, doc.Id, $"cce-{doc.Number}-{seq}.xml", result.XmlContent, ct);
        }
        doc.UpdatedAt = Now;
        Audit.Event(db, doc, Now, "Carta de correção", $"Nº {seq}: {Label(result.Outcome)}{Suffix(result)} Texto: {text}", user);
        Audit.Log(db, Now, doc.CompanyId, user, "Carta de correção", "Documento", doc.Id, result.Outcome.ToString());
        await db.SaveChangesAsync(ct);
        return new OperationResult(ok, true, ok ? $"Carta de correção nº {seq} registrada." : $"Carta de correção não registrada: {Label(result.Outcome)}{Suffix(result)}", []);
    }

    private async Task<string?> SubstitutedKeyAsync(FiscalDocument doc, CancellationToken ct) =>
        doc.SubstitutesDocumentId is { } id
            ? await db.Documents.Where(d => d.Id == id && d.CompanyId == doc.CompanyId).Select(d => d.AccessKey).FirstOrDefaultAsync(ct)
            : null;

    private async Task MarkAuthorizedAsync(FiscalDocument doc, ProviderResult result, string user, CancellationToken ct)
    {
        doc.Status = DocumentStatus.Autorizada;
        doc.AuthorizedAt = result.AuthorizedAt ?? Now;
        doc.AccessKey = result.AccessKey ?? doc.AccessKey;
        doc.Protocol = result.Protocol ?? doc.Protocol;
        doc.OfficialNumber = result.OfficialNumber ?? doc.OfficialNumber;
        doc.VerificationCode = result.VerificationCode ?? doc.VerificationCode;
        doc.LastErrorCode = null;
        doc.LastErrorMessage = null;
        if (!string.IsNullOrEmpty(result.XmlContent))
        {
            var prefix = doc.Type == FiscalDocumentType.Nfe ? "nfe" : "nfse";
            doc.XmlPath = await files.SaveAsync(doc.CompanyId, doc.Id, $"{prefix}-{doc.Series}-{doc.Number}-autorizada.xml", result.XmlContent, ct);
        }

        if (doc.SubstitutesDocumentId is { } originalId)
        {
            var original = await db.Documents.FirstOrDefaultAsync(d => d.Id == originalId && d.CompanyId == doc.CompanyId, ct);
            if (original is not null && original.Status == DocumentStatus.Autorizada)
            {
                original.Status = DocumentStatus.Cancelada;
                original.CancelledAt = Now;
                original.CancelReason = $"Substituída pela nota #{doc.Id}.";
                original.SubstitutedByDocumentId = doc.Id;
                original.UpdatedAt = Now;
                Audit.Event(db, original, Now, "Substituição", $"Cancelada por substituição pela nota #{doc.Id}.", user);
            }
        }
    }

    private static void MarkRejected(FiscalDocument doc, ProviderResult result)
    {
        doc.Status = DocumentStatus.Rejeitada;
        doc.LastErrorCode = result.Code;
        doc.LastErrorMessage = result.Message;
    }

    private async Task<int> ReserveNumberAsync(int companyId, FiscalDocumentType type, CancellationToken ct)
    {
        await NumberLock.WaitAsync(ct);
        try
        {
            if (type == FiscalDocumentType.Nfe)
            {
                var next = await db.Companies.Where(c => c.Id == companyId).Select(c => c.NfeNextNumber).FirstAsync(ct);
                await db.Companies.Where(c => c.Id == companyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.NfeNextNumber, next + 1), ct);
                return next;
            }
            else
            {
                var next = await db.Companies.Where(c => c.Id == companyId).Select(c => c.NfseNextNumber).FirstAsync(ct);
                await db.Companies.Where(c => c.Id == companyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.NfseNextNumber, next + 1), ct);
                return next;
            }
        }
        finally { NumberLock.Release(); }
    }

    private async Task<ProviderResult> CallAsync(Func<Task<ProviderResult>> call, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TransmissionTimeoutSeconds));
        try
        {
            return await call().WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.Timeout,
                Message = $"Sem resposta em {options.TransmissionTimeoutSeconds}s. O resultado é desconhecido.",
            };
        }
        catch (Exception ex)
        {
            return new ProviderResult { Outcome = TransmissionOutcome.ErroInterno, Message = ex.Message, Details = ex.ToString() };
        }
    }

    private TransmissionAttempt NewAttempt(FiscalDocument doc, TransmissionOperation op, string user, int? batchId, Company company) => new()
    {
        CompanyId = company.Id,
        FiscalDocumentId = doc.Id,
        BatchId = batchId,
        Operation = op,
        DocumentType = doc.Type,
        Environment = doc.Environment,
        Provider = doc.Provider,
        StartedAt = Now,
        UserName = user,
    };

    private void Fill(TransmissionAttempt a, ProviderResult r)
    {
        a.FinishedAt = Now;
        a.Outcome = r.Outcome;
        a.Code = r.Code;
        a.Message = r.Message;
        a.Details = r.Details;
        a.Protocol = r.Protocol;
        a.RequestPayload = r.RequestPayload;
        a.ResponsePayload = r.ResponsePayload;
        a.Summary = AttemptSummaries.For(r.Outcome);
    }

    private static string IssuerSnapshot(Company c) => JsonSerializer.Serialize(new
    {
        c.Id, c.Cnpj, c.LegalName, c.TradeName, c.StateRegistration, c.MunicipalRegistration,
        c.TaxRegime, c.Crt, c.Address, c.NfeSeries, c.NfseSeries, c.NfeEnvironment, c.NfseEnvironment,
    });

    private async Task<string?> CustomerSnapshotAsync(int? customerId)
    {
        if (customerId is null) return null;
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId);
        return c is null ? null : JsonSerializer.Serialize(new
        {
            c.Id, c.Document, c.Name, c.IeIndicator, c.StateRegistration, c.MunicipalRegistration, c.Email, c.Phone, c.Address,
        });
    }

    private static string Label(FiscalDocument d) =>
        $"{(d.Type == FiscalDocumentType.Nfe ? "NF-e" : "NFS-e")} {d.DisplayNumber}, {EnumText.Of(d.Environment)}{(d.IsSimulated ? ", SIMULAÇÃO" : "")}";

    private static string Label(TransmissionOutcome o) => AttemptSummaries.Title(o);

    private static string Suffix(ProviderResult r) =>
        string.IsNullOrEmpty(r.Code) && string.IsNullOrEmpty(r.Message) ? "."
            : " — " + string.Join(" ", new[] { r.Code, r.Message }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Textos de orientação da aplicação (não são mensagens oficiais).</summary>
public static class AttemptSummaries
{
    public static string Title(TransmissionOutcome o) => o switch
    {
        TransmissionOutcome.Autorizada => "Autorizada",
        TransmissionOutcome.Rejeitada => "Rejeitada pelo serviço fiscal",
        TransmissionOutcome.EmProcessamento => "Em processamento",
        TransmissionOutcome.NaoEncontrada => "Não encontrada",
        TransmissionOutcome.EventoRegistrado => "Evento registrado",
        TransmissionOutcome.EventoRejeitado => "Evento rejeitado",
        TransmissionOutcome.ValidacaoLocal => "Bloqueada na validação local",
        TransmissionOutcome.FalhaCertificado => "Falha de certificado (nada enviado)",
        TransmissionOutcome.Indisponivel => "Serviço indisponível (nada enviado)",
        TransmissionOutcome.Timeout => "Timeout — resultado desconhecido",
        TransmissionOutcome.ErroInterno => "Erro inesperado — resultado desconhecido",
        _ => o.ToString(),
    };

    public static string For(TransmissionOutcome o) => o switch
    {
        TransmissionOutcome.Rejeitada => "O serviço fiscal recusou o documento. Leia a mensagem oficial, corrija o rascunho/cadastro indicado e emita novamente.",
        TransmissionOutcome.EmProcessamento => "Aguarde e use 'Consultar situação' para obter o resultado.",
        TransmissionOutcome.ValidacaoLocal => "A aplicação encontrou pendências antes do envio. Nada foi transmitido.",
        TransmissionOutcome.FalhaCertificado => "Verifique o certificado A1 da empresa (validade, senha, CNPJ). Nada foi transmitido.",
        TransmissionOutcome.Indisponivel => "O serviço não aceitou a conexão. Tente mais tarde.",
        TransmissionOutcome.Timeout => "Não reenvie: use 'Consultar situação' para descobrir se o documento foi processado.",
        TransmissionOutcome.ErroInterno => "Use 'Consultar situação' antes de qualquer nova tentativa.",
        TransmissionOutcome.EventoRejeitado => "O evento foi recusado. Confira prazo, justificativa e mensagem oficial.",
        _ => "",
    };
}
