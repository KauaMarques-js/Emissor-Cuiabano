namespace FiscalPlatform.Domain;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class Address
{
    public string? ZipCode { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? Complement { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    /// <summary>Código IBGE do município (7 dígitos).</summary>
    public string? CityIbgeCode { get; set; }
}

public class Company
{
    public const string CuiabaIbgeCode = "5103403";

    public int Id { get; set; }

    // Identificação
    public string Cnpj { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string? TradeName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();

    public string? StateRegistration { get; set; }
    public string? MunicipalRegistration { get; set; }

    /// <summary>Regime tributário (conferir com o contador).</summary>
    public TaxRegime TaxRegime { get; set; }

    /// <summary>CRT da NF-e, derivado do regime: 1 Simples, 2 Simples com excesso, 3 Normal, 4 MEI.</summary>
    public int? Crt => TaxRegime switch
    {
        TaxRegime.SimplesNacional => 1,
        TaxRegime.SimplesNacionalExcesso => 2,
        TaxRegime.RegimeNormal => 3,
        TaxRegime.Mei => 4,
        _ => null,
    };

    // NF-e 55
    public bool NfeEnabled { get; set; }
    public int NfeSeries { get; set; } = 1;
    public int NfeNextNumber { get; set; } = 1;
    public FiscalEnvironment NfeEnvironment { get; set; } = FiscalEnvironment.Homologacao;
    public ProviderKind NfeProvider { get; set; } = ProviderKind.Simulacao;

    // NFS-e
    public bool NfseEnabled { get; set; }
    public int NfseSeries { get; set; } = 1;
    public int NfseNextNumber { get; set; } = 1;
    public FiscalEnvironment NfseEnvironment { get; set; } = FiscalEnvironment.Homologacao;
    public ProviderKind NfseProvider { get; set; } = ProviderKind.Simulacao;
    /// <summary>Regime especial de tributação municipal (regEspTrib da DPS): 0 nenhum, 1 cooperativa, 2 estimativa, 3 ME municipal, 4 notário, 5 autônomo, 6 sociedade de profissionais, 9 outros.</summary>
    public int NfseSpecialRegime { get; set; }
    /// <summary>Percentual aproximado de tributos do Simples Nacional (pTotTribSN), exigido para ME/EPP.</summary>
    public decimal? NfseSimplesTaxPercent { get; set; }
    /// <summary>Percentuais aproximados de tributos federais/municipais (Lei 12.741/2012), usados por não optantes do Simples.</summary>
    public decimal? NfseFederalTaxPercent { get; set; }
    public decimal? NfseMunicipalTaxPercent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(TradeName) ? LegalName : TradeName!;

    public FiscalEnvironment EnvironmentFor(FiscalDocumentType type) =>
        type == FiscalDocumentType.Nfe ? NfeEnvironment : NfseEnvironment;

    public ProviderKind ProviderFor(FiscalDocumentType type) =>
        type == FiscalDocumentType.Nfe ? NfeProvider : NfseProvider;

    public EmissionMode ModeFor(FiscalDocumentType type) =>
        ProviderFor(type) == ProviderKind.Simulacao ? EmissionMode.Simulacao
        : EnvironmentFor(type) == FiscalEnvironment.Producao ? EmissionMode.Producao
        : EmissionMode.Homologacao;

    public void SetMode(FiscalDocumentType type, EmissionMode mode)
    {
        var provider = mode == EmissionMode.Simulacao ? ProviderKind.Simulacao : ProviderKind.Oficial;
        var environment = mode == EmissionMode.Producao ? FiscalEnvironment.Producao : FiscalEnvironment.Homologacao;
        if (type == FiscalDocumentType.Nfe) (NfeProvider, NfeEnvironment) = (provider, environment);
        else (NfseProvider, NfseEnvironment) = (provider, environment);
    }
}

public class CertificateMetadata
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public string? CertificateCnpj { get; set; }
    public bool CnpjMatches { get; set; }
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public DateTime ImportedAt { get; set; }
    public string? ImportedBy { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ReplacedAt { get; set; }
    /// <summary>Arquivo .pfx criptografado no diretório de dados (removido quando substituído).</summary>
    public string? StoredFileName { get; set; }
    /// <summary>Senha protegida pela Data Protection API; nunca em texto puro.</summary>
    public string? ProtectedPassword { get; set; }
    public string? ValidationNotes { get; set; }

    public CertificateStatus StatusAt(DateTime now, int warningDays)
    {
        if (!CnpjMatches) return CertificateStatus.Invalido;
        if (now > NotAfter || now < NotBefore) return CertificateStatus.Expirado;
        if (NotAfter - now <= TimeSpan.FromDays(warningDays)) return CertificateStatus.ProximoVencimento;
        return CertificateStatus.Valido;
    }
}

public class Customer
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    /// <summary>CPF ou CNPJ, somente dígitos.</summary>
    public string Document { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Indicador IE do destinatário: 1 contribuinte, 2 isento, 9 não contribuinte.</summary>
    public int IeIndicator { get; set; } = 9;
    public string? StateRegistration { get; set; }
    public string? MunicipalRegistration { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "UN";
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    /// <summary>Origem da mercadoria (0 a 8).</summary>
    public int Origin { get; set; }
    public string? Gtin { get; set; }
    public decimal Price { get; set; }
    public string? Cfop { get; set; }
    /// <summary>CST (regime normal) ou CSOSN (Simples Nacional) do ICMS.</summary>
    public string? IcmsTaxCode { get; set; }
    public decimal IcmsRate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Service
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Código de tributação nacional (cTribNac, 6 dígitos).</summary>
    public string? NationalTaxCode { get; set; }
    public string? MunicipalTaxCode { get; set; }
    public string? NbsCode { get; set; }
    public string? IncidenceMunicipalityIbge { get; set; }
    public decimal Price { get; set; }
    public decimal IssRate { get; set; }
    public bool IssWithheld { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AuditLog
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
