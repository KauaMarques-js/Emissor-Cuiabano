using FiscalPlatform.Domain;

namespace FiscalPlatform.Web.Models;

/// <summary>Formulário da empresa, usado no cadastro inicial e na edição.</summary>
public class CompanyInput
{
    public string Cnpj { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string? TradeName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public string? StateRegistration { get; set; }
    public string? MunicipalRegistration { get; set; }
    public TaxRegime TaxRegime { get; set; }

    public bool IssuesNfse { get; set; } = true;
    public bool IssuesNfe { get; set; }
    public EmissionMode NfseMode { get; set; }
    public int NfseSpecialRegime { get; set; }
    public decimal? NfseSimplesTaxPercent { get; set; }
    public decimal? NfseFederalTaxPercent { get; set; }
    public decimal? NfseMunicipalTaxPercent { get; set; }
    public int NfseSeries { get; set; } = 1;
    public int NfseNextNumber { get; set; } = 1;
    public int NfeSeries { get; set; } = 1;
    public int NfeNextNumber { get; set; } = 1;

    public IFormFile? CertificateFile { get; set; }
    public string? CertificatePassword { get; set; }

    public static CompanyInput From(Company c) => new()
    {
        Cnpj = DocumentNumbers.Format(c.Cnpj), LegalName = c.LegalName, TradeName = c.TradeName, Email = c.Email, Phone = c.Phone,
        Address = c.Address, StateRegistration = c.StateRegistration, MunicipalRegistration = c.MunicipalRegistration,
        TaxRegime = c.TaxRegime, IssuesNfse = c.NfseEnabled, IssuesNfe = c.NfeEnabled, NfseMode = c.ModeFor(FiscalDocumentType.Nfse),
        NfseSpecialRegime = c.NfseSpecialRegime, NfseSimplesTaxPercent = c.NfseSimplesTaxPercent,
        NfseFederalTaxPercent = c.NfseFederalTaxPercent, NfseMunicipalTaxPercent = c.NfseMunicipalTaxPercent,
        NfseSeries = c.NfseSeries, NfseNextNumber = c.NfseNextNumber, NfeSeries = c.NfeSeries, NfeNextNumber = c.NfeNextNumber,
    };

    public void ApplyTo(Company c)
    {
        c.Cnpj = Cnpj;
        c.LegalName = LegalName.Trim();
        c.TradeName = TradeName?.Trim();
        c.Email = Email?.Trim();
        c.Phone = Phone;
        c.Address = Address;
        c.StateRegistration = StateRegistration?.Trim();
        c.MunicipalRegistration = MunicipalRegistration?.Trim();
        c.TaxRegime = TaxRegime;
        c.NfseEnabled = IssuesNfse;
        c.NfeEnabled = IssuesNfe;
        c.SetMode(FiscalDocumentType.Nfse, NfseMode);
        // A emissão oficial de NF-e ainda não existe: NF-e sempre em simulação.
        c.SetMode(FiscalDocumentType.Nfe, EmissionMode.Simulacao);
        c.NfseSpecialRegime = NfseSpecialRegime;
        c.NfseSimplesTaxPercent = NfseSimplesTaxPercent;
        c.NfseFederalTaxPercent = NfseFederalTaxPercent;
        c.NfseMunicipalTaxPercent = NfseMunicipalTaxPercent;
        c.NfseSeries = NfseSeries;
        c.NfseNextNumber = NfseNextNumber;
        c.NfeSeries = NfeSeries;
        c.NfeNextNumber = NfeNextNumber;
    }
}

public class RegisterInput
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public CompanyInput Company { get; set; } = new()
    {
        Address = new Address { City = "Cuiabá", State = "MT", CityIbgeCode = Domain.Company.CuiabaIbgeCode },
    };
}

public class LoginInput
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
