using FiscalPlatform.Application;
using FiscalPlatform.Domain;

namespace FiscalPlatform.Web.Models;

public class DocumentFilter
{
    public FiscalDocumentType? Type { get; set; }
    public DocumentStatus? Status { get; set; }
    public FiscalEnvironment? Environment { get; set; }
    /// <summary>Número, chave, protocolo, cliente ou CPF/CNPJ.</summary>
    public string? Q { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? BatchId { get; set; }
    public bool Archived { get; set; }
    /// <summary>Somente notas que precisam de ação (rejeitadas, pendentes ou sem resposta).</summary>
    public bool Attention { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>Formulário de emissão. Os itens chegam como listas paralelas, uma posição por linha.</summary>
public class NoteInput
{
    public int? DocumentId { get; set; }
    public FiscalDocumentType Type { get; set; } = FiscalDocumentType.Nfse;
    public int? CustomerId { get; set; }
    public Customer NewCustomer { get; set; } = new();
    public List<string?> LineItem { get; set; } = [];
    public List<string?> LineQty { get; set; } = [];
    public List<string?> LinePrice { get; set; } = [];
    public List<string?> LineDiscount { get; set; } = [];
    public string? ServiceDescription { get; set; }
    public DateOnly? CompetenceDate { get; set; }
    public bool IssWithheld { get; set; }
    public string? NatureOfOperation { get; set; } = "Venda de mercadoria";
    public decimal Freight { get; set; }
    public string? AdditionalInfo { get; set; }
}

public class NoteFormModel
{
    public required NoteInput Input { get; init; }
    public required Company Company { get; init; }
    public required List<Customer> Customers { get; init; }
    public required List<Product> Products { get; init; }
    public required List<Service> Services { get; init; }
    public required IReadOnlyList<ValidationError> Errors { get; init; }
    public FiscalDocument? Document { get; init; }
}

public class NotePageModel
{
    public required FiscalDocument Document { get; init; }
    public required List<TransmissionAttempt> Attempts { get; init; }
    public required List<FiscalEvent> Events { get; init; }
    public required List<ValidationError> Pending { get; init; }
    public required bool CanCancel { get; init; }
}
