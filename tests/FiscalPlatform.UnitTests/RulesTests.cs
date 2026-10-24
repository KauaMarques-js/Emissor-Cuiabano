using FiscalPlatform.Application;
using FiscalPlatform.Domain;

namespace FiscalPlatform.UnitTests;

public class DocumentNumbersTests
{
    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11222333000182", false)]
    [InlineData("00000000000000", false)]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224726", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("", false)]
    public void Validates_cpf_and_cnpj(string value, bool expected) =>
        Assert.Equal(expected, DocumentNumbers.IsValidCpfOrCnpj(value));

    [Fact]
    public void Formats_documents()
    {
        Assert.Equal("11.222.333/0001-81", DocumentNumbers.Format("11222333000181"));
        Assert.Equal("529.982.247-25", DocumentNumbers.Format("52998224725"));
    }
}

public class CfopTests
{
    [Theory]
    [InlineData("MT", "MT", '5')]
    [InlineData("MT", "SP", '6')]
    [InlineData("MT", "EX", '7')]
    public void Expected_prefix_depends_on_destination(string origin, string destination, char expected) =>
        Assert.Equal(expected, DocumentValidator.ExpectedCfopPrefix(origin, destination));
}

public class DocumentRulesTests
{
    [Fact]
    public void Draft_can_be_edited_and_deleted_until_number_is_reserved()
    {
        var doc = new FiscalDocument { Status = DocumentStatus.Rascunho };
        Assert.True(DocumentRules.CanEdit(doc));
        Assert.True(DocumentRules.CanDelete(doc));
        doc.Number = 10;
        Assert.False(DocumentRules.CanDelete(doc));
        Assert.True(DocumentRules.CanArchive(doc));
    }

    [Theory]
    [InlineData(DocumentStatus.Autorizada)]
    [InlineData(DocumentStatus.Cancelada)]
    [InlineData(DocumentStatus.FalhaComunicacao)]
    [InlineData(DocumentStatus.Pendente)]
    [InlineData(DocumentStatus.Enviando)]
    public void Non_draft_states_are_not_editable(DocumentStatus status) =>
        Assert.False(DocumentRules.CanEdit(new FiscalDocument { Status = status }));

    [Fact]
    public void Nfe_cancel_respects_deadline()
    {
        var now = new DateTime(2026, 10, 24, 12, 0, 0);
        var doc = new FiscalDocument { Type = FiscalDocumentType.Nfe, Status = DocumentStatus.Autorizada, AuthorizedAt = now.AddHours(-23) };
        Assert.True(DocumentRules.CanCancel(doc, now, 24));
        doc.AuthorizedAt = now.AddHours(-25);
        Assert.False(DocumentRules.CanCancel(doc, now, 24));
    }

    [Fact]
    public void Correction_letter_and_substitution_are_type_specific()
    {
        var nfe = new FiscalDocument { Type = FiscalDocumentType.Nfe, Status = DocumentStatus.Autorizada };
        var nfse = new FiscalDocument { Type = FiscalDocumentType.Nfse, Status = DocumentStatus.Autorizada };
        Assert.True(DocumentRules.CanSendCorrectionLetter(nfe));
        Assert.False(DocumentRules.CanSendCorrectionLetter(nfse));
        Assert.False(DocumentRules.CanSubstitute(nfe));
        Assert.True(DocumentRules.CanSubstitute(nfse));
    }

    [Fact]
    public void Totals_are_calculated_from_items()
    {
        var doc = new FiscalDocument { Type = FiscalDocumentType.Nfe };
        doc.Nfe.Freight = 10;
        doc.Items.Add(new FiscalDocumentItem { Quantity = 2, UnitPrice = 50.555m, Discount = 1, TaxRate = 18 });
        doc.Recalculate();
        Assert.Equal(101.11m, doc.TotalItems);
        Assert.Equal(1m, doc.TotalDiscount);
        Assert.Equal(110.11m, doc.TotalAmount);
        Assert.Equal(18.02m, doc.TotalTaxes);
    }
}

public class ValidatorTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 10, 0, 0);

    [Fact]
    public void Ready_company_has_no_blocking_items_in_simulation()
    {
        var items = CompanyReadiness.Evaluate(TestData.Company(), null, Now, 30);
        Assert.Empty(items.BlockingFor(FiscalDocumentType.Nfe));
        Assert.Empty(items.BlockingFor(FiscalDocumentType.Nfse));
    }

    [Fact]
    public void Official_nfse_requires_valid_certificate()
    {
        var company = TestData.Company();
        company.SetMode(FiscalDocumentType.Nfse, EmissionMode.Homologacao);
        company.NfseSimplesTaxPercent = 6;
        var blocking = CompanyReadiness.Evaluate(company, null, Now, 30).BlockingFor(FiscalDocumentType.Nfse).ToList();
        Assert.Contains(blocking, i => i.Area == "Certificado");

        var expired = new CertificateMetadata { CnpjMatches = true, NotBefore = Now.AddYears(-2), NotAfter = Now.AddDays(-1) };
        blocking = CompanyReadiness.Evaluate(company, expired, Now, 30).BlockingFor(FiscalDocumentType.Nfse).ToList();
        Assert.Single(blocking);
        Assert.Contains("expirado", blocking[0].Message);
    }

    [Fact]
    public void Official_nfe_is_not_available_yet()
    {
        var company = TestData.Company();
        company.NfeProvider = ProviderKind.Oficial;
        Assert.Contains(CompanyReadiness.Evaluate(company, null, Now, 30).BlockingFor(FiscalDocumentType.Nfe), i => i.Message.Contains("ainda não está disponível"));
    }

    [Theory]
    [InlineData(TaxRegime.SimplesNacional, 1)]
    [InlineData(TaxRegime.SimplesNacionalExcesso, 2)]
    [InlineData(TaxRegime.RegimeNormal, 3)]
    [InlineData(TaxRegime.Mei, 4)]
    public void Crt_is_derived_from_tax_regime(TaxRegime regime, int crt) =>
        Assert.Equal(crt, new Company { TaxRegime = regime }.Crt);

    [Theory]
    [InlineData(EmissionMode.Simulacao)]
    [InlineData(EmissionMode.Homologacao)]
    [InlineData(EmissionMode.Producao)]
    public void Emission_mode_round_trips(EmissionMode mode)
    {
        var company = new Company();
        company.SetMode(FiscalDocumentType.Nfse, mode);
        Assert.Equal(mode, company.ModeFor(FiscalDocumentType.Nfse));
    }

    [Fact]
    public void Simples_nacional_requires_csosn_and_matching_cfop()
    {
        var company = TestData.Company();
        var customer = TestData.Customer(company.Id);
        customer.Address.State = "SP";
        var doc = TestData.NfeDraft(company.Id, customer.Id);
        doc.Items[0].IcmsTaxCode = "00";
        doc.Recalculate();
        var errors = DocumentValidator.Validate(doc, company, customer, null, Now, 30);
        Assert.Contains(errors, e => e.Message.Contains("CSOSN"));
        Assert.Contains(errors, e => e.Message.Contains("CFOP 5102 incompatível"));
    }

    [Fact]
    public void Nfse_must_have_exactly_one_service()
    {
        var company = TestData.Company();
        var customer = TestData.Customer(company.Id);
        var doc = TestData.NfseDraft(company.Id, customer.Id);
        doc.Items.Add(doc.Items[0]);
        doc.Recalculate();
        Assert.Contains(DocumentValidator.Validate(doc, company, customer, null, Now, 30), e => e.Message.Contains("exatamente um serviço"));
    }

    [Fact]
    public void Customer_of_another_company_is_rejected()
    {
        var company = TestData.Company();
        var customer = TestData.Customer(company.Id + 1);
        var doc = TestData.NfeDraft(company.Id, customer.Id);
        doc.Recalculate();
        Assert.Contains(DocumentValidator.Validate(doc, company, customer, null, Now, 30), e => e.Message.Contains("outra empresa"));
    }
}
