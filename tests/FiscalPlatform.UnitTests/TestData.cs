using FiscalPlatform.Domain;

namespace FiscalPlatform.UnitTests;

internal static class TestData
{
    public static Company Company(string cnpj = "11222333000181") => new()
    {
        Id = 1,
        Cnpj = cnpj,
        LegalName = "Empresa Teste LTDA",
        Address = new Address
        {
            ZipCode = "78000000", Street = "Rua A", Number = "10", District = "Centro",
            City = "Cuiabá", State = "MT", CityIbgeCode = "5103403",
        },
        StateRegistration = "131234567",
        MunicipalRegistration = "12345",
        TaxRegime = TaxRegime.SimplesNacional,
        NfeEnabled = true,
        NfseEnabled = true,
        NfeProvider = ProviderKind.Simulacao,
        NfseProvider = ProviderKind.Simulacao,
    };

    public static Customer Customer(int companyId) => new()
    {
        Id = 1,
        CompanyId = companyId,
        Document = "52998224725",
        Name = "Cliente Teste",
        IeIndicator = 9,
        Address = new Address { Street = "Rua B", Number = "20", District = "Bairro", City = "Cuiabá", State = "MT", CityIbgeCode = "5103403" },
    };

    public static FiscalDocument NfeDraft(int companyId, int customerId) => new()
    {
        CompanyId = companyId,
        CustomerId = customerId,
        Type = FiscalDocumentType.Nfe,
        Items =
        [
            new FiscalDocumentItem
            {
                LineNumber = 1, Code = "P1", Description = "Perfume 100ml", Unit = "UN", Ncm = "33030010",
                Cfop = "5102", IcmsTaxCode = "102", Quantity = 2, UnitPrice = 150,
            },
        ],
    };

    public static FiscalDocument NfseDraft(int companyId, int customerId) => new()
    {
        CompanyId = companyId,
        CustomerId = customerId,
        Type = FiscalDocumentType.Nfse,
        Nfse = new NfseData { CompetenceDate = new DateOnly(2026, 10, 1), IncidenceMunicipalityIbge = "5103403" },
        Items =
        [
            new FiscalDocumentItem
            {
                LineNumber = 1, Code = "S1", Description = "Desenvolvimento de software", NationalTaxCode = "010101",
                Quantity = 1, UnitPrice = 1000, TaxRate = 2,
            },
        ],
    };
}
