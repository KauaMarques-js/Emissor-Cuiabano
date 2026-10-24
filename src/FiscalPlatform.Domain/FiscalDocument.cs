namespace FiscalPlatform.Domain;

/// <summary>Dados exclusivos da NF-e modelo 55.</summary>
public class NfeData
{
    public string? NatureOfOperation { get; set; } = "Venda de mercadoria";
    public decimal Freight { get; set; }
}

/// <summary>Dados exclusivos da NFS-e (DPS).</summary>
public class NfseData
{
    public DateOnly? CompetenceDate { get; set; }
    public string? IncidenceMunicipalityIbge { get; set; }
    public bool IssWithheld { get; set; }
}

public class FiscalDocument
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public FiscalDocumentType Type { get; set; }
    public DocumentStatus Status { get; set; }
    public FiscalEnvironment Environment { get; set; }
    public ProviderKind Provider { get; set; }

    public int? Series { get; set; }
    /// <summary>Número reservado na primeira transmissão e reutilizado nas novas tentativas.</summary>
    public int? Number { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Fotografia dos dados do emitente e do destinatário usados na emissão.</summary>
    public string? IssuerSnapshotJson { get; set; }
    public string? CustomerSnapshotJson { get; set; }

    public NfeData Nfe { get; set; } = new();
    public NfseData Nfse { get; set; } = new();

    public string? AdditionalInfo { get; set; }

    public decimal TotalItems { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal TotalAmount { get; set; }

    // Retorno oficial
    public string? AccessKey { get; set; }
    public string? Protocol { get; set; }
    public string? OfficialNumber { get; set; }
    public string? VerificationCode { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public string? XmlPath { get; set; }

    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? CancelProtocol { get; set; }
    public int CorrectionLetterCount { get; set; }

    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
    public int? SourceDocumentId { get; set; }
    public int? SubstitutesDocumentId { get; set; }
    public int? SubstitutedByDocumentId { get; set; }
    public int? LastBatchId { get; set; }
    public bool Archived { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<FiscalDocumentItem> Items { get; set; } = new();

    public bool IsSimulated => Provider == ProviderKind.Simulacao;

    public string DisplayNumber => Number is null ? $"#{Id} (sem número)" : $"{Series}-{Number}";

    public void Recalculate()
    {
        var line = 1;
        foreach (var item in Items.OrderBy(i => i.LineNumber))
        {
            item.LineNumber = line++;
            item.Recalculate();
        }
        TotalItems = Items.Sum(i => i.GrossAmount);
        TotalDiscount = Items.Sum(i => i.Discount);
        TotalTaxes = Items.Sum(i => i.TaxValue);
        TotalAmount = TotalItems - TotalDiscount + (Type == FiscalDocumentType.Nfe ? Nfe.Freight : 0m);
    }

    /// <summary>Valor líquido da NFS-e quando o ISS é retido pelo tomador.</summary>
    public decimal NetAmount => Type == FiscalDocumentType.Nfse && Nfse.IssWithheld ? TotalAmount - TotalTaxes : TotalAmount;
}

public class FiscalDocumentItem
{
    public int Id { get; set; }
    public int FiscalDocumentId { get; set; }
    public int LineNumber { get; set; }
    public int? ProductId { get; set; }
    public int? ServiceId { get; set; }

    // Fotografia do cadastro no momento da inclusão
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Unit { get; set; }
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    public string? Cfop { get; set; }
    public int Origin { get; set; }
    public string? Gtin { get; set; }
    public string? IcmsTaxCode { get; set; }
    public string? NationalTaxCode { get; set; }
    public string? MunicipalTaxCode { get; set; }
    public string? NbsCode { get; set; }

    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    /// <summary>Alíquota de ICMS (NF-e) ou ISS (NFS-e), conforme cadastro.</summary>
    public decimal TaxRate { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal Total { get; set; }
    public decimal TaxValue { get; set; }

    public void Recalculate()
    {
        GrossAmount = Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
        Total = GrossAmount - Discount;
        TaxValue = Math.Round(Total * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
    }
}

public class FiscalEvent
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int FiscalDocumentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string? UserName { get; set; }
}

public class TransmissionAttempt
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int FiscalDocumentId { get; set; }
    public FiscalDocument? FiscalDocument { get; set; }
    public int? BatchId { get; set; }
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public TransmissionOperation Operation { get; set; }
    public FiscalDocumentType DocumentType { get; set; }
    public FiscalEnvironment Environment { get; set; }
    public ProviderKind Provider { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public TransmissionOutcome Outcome { get; set; }
    /// <summary>Código exatamente como retornado pelo serviço.</summary>
    public string? Code { get; set; }
    /// <summary>Mensagem integral retornada pelo serviço, sem substituição.</summary>
    public string? Message { get; set; }
    public string? Details { get; set; }
    public string? Protocol { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    /// <summary>Resumo e sugestão gerados pela aplicação (não oficiais).</summary>
    public string? Summary { get; set; }
    public string? UserName { get; set; }

    public bool IsProblem => Outcome is not (TransmissionOutcome.Autorizada or TransmissionOutcome.EventoRegistrado);
}

public class EmissionBatch
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public FiscalDocumentType DocumentType { get; set; }
    public FiscalEnvironment Environment { get; set; }
    public ProviderKind Provider { get; set; }
    public BatchStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? CreatedBy { get; set; }
    public List<BatchItem> Items { get; set; } = new();
}

public class BatchItem
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public int FiscalDocumentId { get; set; }
    public FiscalDocument? FiscalDocument { get; set; }
    public BatchItemStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
