using FiscalPlatform.Domain;
using static FiscalPlatform.Domain.DocumentNumbers;

namespace FiscalPlatform.Application;

public sealed record ValidationError(string Field, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Field) ? Message : $"{Field}: {Message}";
}

public sealed record ReadinessItem(FiscalDocumentType? AppliesTo, bool Blocking, string Area, string Message);

/// <summary>Checklist de prontidão da empresa para emitir cada tipo de documento.</summary>
public static class CompanyReadiness
{
    public static List<ReadinessItem> Evaluate(Company c, CertificateMetadata? cert, DateTime now, int warningDays)
    {
        var items = new List<ReadinessItem>();
        void Block(FiscalDocumentType? t, string area, string msg) => items.Add(new(t, true, area, msg));
        void Warn(FiscalDocumentType? t, string area, string msg) => items.Add(new(t, false, area, msg));

        if (!c.IsActive) Block(null, "Empresa", "Empresa está inativa.");
        if (!IsValidCnpj(c.Cnpj)) Block(null, "Identificação", "CNPJ inválido.");
        if (string.IsNullOrWhiteSpace(c.LegalName)) Block(null, "Identificação", "Informe a razão social.");

        var a = c.Address;
        var missing = new List<string>();
        if (!IsDigits(a.ZipCode, 8)) missing.Add("CEP");
        if (string.IsNullOrWhiteSpace(a.Street)) missing.Add("logradouro");
        if (string.IsNullOrWhiteSpace(a.Number)) missing.Add("número");
        if (string.IsNullOrWhiteSpace(a.District)) missing.Add("bairro");
        if (string.IsNullOrWhiteSpace(a.City)) missing.Add("município");
        if (a.State?.Length != 2) missing.Add("UF");
        if (!IsDigits(a.CityIbgeCode, 7)) missing.Add("código IBGE do município");
        if (missing.Count > 0) Block(null, "Endereço", "Falta: " + string.Join(", ", missing) + ".");

        if (c.TaxRegime == TaxRegime.NaoInformado) Block(null, "Regime", "Informe o regime tributário (confirme com o contador).");

        const FiscalDocumentType nfe = FiscalDocumentType.Nfe;
        if (!c.NfeEnabled) Block(nfe, "NF-e", "A empresa não está configurada para emitir NF-e.");
        else
        {
            if (string.IsNullOrWhiteSpace(c.StateRegistration)) Block(nfe, "Inscrições", "Informe a inscrição estadual (necessária para NF-e).");
            if (c.NfeSeries is < 0 or > 999) Block(nfe, "NF-e", "Série da NF-e deve estar entre 0 e 999.");
            if (c.NfeNextNumber < 1) Block(nfe, "NF-e", "Próximo número da NF-e deve ser maior que zero.");
            if (c.NfeProvider == ProviderKind.Oficial) Block(nfe, "NF-e", "A emissão oficial de NF-e ainda não está disponível; use a simulação.");
            else Warn(nfe, "Modo", "NF-e em simulação: as notas não têm valor fiscal.");
        }

        const FiscalDocumentType nfse = FiscalDocumentType.Nfse;
        if (!c.NfseEnabled) Block(nfse, "NFS-e", "A empresa não está configurada para emitir NFS-e.");
        else
        {
            if (string.IsNullOrWhiteSpace(c.MunicipalRegistration)) Warn(nfse, "Inscrições", "Inscrição municipal não informada; confirme com a prefeitura se é exigida.");
            if (c.NfseProvider == ProviderKind.Simulacao) Warn(nfse, "Modo", "NFS-e em simulação: as notas não têm valor fiscal.");
            else
            {
                var certProblem = CertificateProblem(cert, now, warningDays, out var nearExpiry);
                if (certProblem is not null) Block(nfse, "Certificado", certProblem);
                else if (nearExpiry) Warn(nfse, "Certificado", $"O certificado vence em {cert!.NotAfter:dd/MM/yyyy}. Renove para não parar de emitir.");
                foreach (var problem in NfseRules.CompanyProblems(c)) Block(nfse, "NFS-e", problem);
            }
        }

        return items;
    }

    private static string? CertificateProblem(CertificateMetadata? cert, DateTime now, int warningDays, out bool nearExpiry)
    {
        nearExpiry = false;
        if (cert is null) return "Envie o certificado digital A1 da empresa para emitir de verdade.";
        switch (cert.StatusAt(now, warningDays))
        {
            case CertificateStatus.Invalido: return "CNPJ do certificado não corresponde ao da empresa.";
            case CertificateStatus.Expirado: return $"Certificado expirado/fora da validade ({cert.NotAfter:dd/MM/yyyy}).";
            case CertificateStatus.ProximoVencimento: nearExpiry = true; break;
        }
        return null;
    }

    public static IEnumerable<ReadinessItem> BlockingFor(this IEnumerable<ReadinessItem> items, FiscalDocumentType type) =>
        items.Where(i => i.Blocking && (i.AppliesTo is null || i.AppliesTo == type));
}

/// <summary>Validações conhecidas antes da transmissão. Não substitui a validação oficial (XSD/regras do serviço).</summary>
public static class DocumentValidator
{
    public static List<ValidationError> Validate(FiscalDocument doc, Company company, Customer? customer,
        CertificateMetadata? cert, DateTime now, int warningDays)
    {
        var errors = CompanyReadiness.Evaluate(company, cert, now, warningDays)
            .BlockingFor(doc.Type)
            .Select(i => new ValidationError("Empresa - " + i.Area, i.Message))
            .ToList();

        if (doc.CompanyId != company.Id)
            errors.Add(new("Empresa", "O documento não pertence à empresa selecionada."));

        ValidateCustomer(doc, company, customer, errors);

        if (doc.Type == FiscalDocumentType.Nfe) ValidateNfe(doc, company, customer, errors);
        else
        {
            ValidateNfse(doc, now, errors);
            if (company.NfseProvider == ProviderKind.Oficial) errors.AddRange(NfseRules.DocumentProblems(doc, company));
        }

        if (doc.TotalAmount <= 0) errors.Add(new("Totais", "O valor total deve ser maior que zero."));
        return errors;
    }

    private static void ValidateCustomer(FiscalDocument doc, Company company, Customer? customer, List<ValidationError> errors)
    {
        var label = doc.Type == FiscalDocumentType.Nfe ? "Destinatário" : "Tomador";
        if (customer is null) { errors.Add(new(label, "Selecione o " + label.ToLowerInvariant() + ".")); return; }
        if (customer.CompanyId != company.Id) errors.Add(new(label, "Cadastro pertence a outra empresa."));
        if (!customer.IsActive) errors.Add(new(label, "Cadastro está inativo."));
        if (!IsValidCpfOrCnpj(customer.Document)) errors.Add(new(label, "CPF/CNPJ inválido."));
        if (string.IsNullOrWhiteSpace(customer.Name)) errors.Add(new(label, "Nome/razão social não informado."));

        if (doc.Type == FiscalDocumentType.Nfe)
        {
            var a = customer.Address;
            if (string.IsNullOrWhiteSpace(a.Street) || string.IsNullOrWhiteSpace(a.Number) || string.IsNullOrWhiteSpace(a.District))
                errors.Add(new(label, "Endereço incompleto (logradouro, número e bairro)."));
            if (a.State?.Length != 2) errors.Add(new(label, "UF não informada."));
            if (!IsDigits(a.CityIbgeCode, 7)) errors.Add(new(label, "Código IBGE do município inválido."));
            if (customer.IeIndicator == 1 && string.IsNullOrWhiteSpace(customer.StateRegistration))
                errors.Add(new(label, "Contribuinte do ICMS sem inscrição estadual."));
        }
    }

    private static void ValidateNfe(FiscalDocument doc, Company company, Customer? customer, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(doc.Nfe.NatureOfOperation)) errors.Add(new("Natureza da operação", "Obrigatória."));
        if (doc.Items.Count == 0) errors.Add(new("Itens", "Adicione ao menos um produto."));
        if (doc.Items.Count > 990) errors.Add(new("Itens", "Máximo de 990 itens por NF-e."));
        if (doc.Nfe.Freight < 0) errors.Add(new("Frete", "O frete não pode ser negativo."));

        var expectedCfopPrefix = ExpectedCfopPrefix(company.Address.State, customer?.Address.State);
        var usesCsosn = company.Crt is 1 or 4;

        foreach (var i in doc.Items)
        {
            var f = $"Item {i.LineNumber} ({i.Description})";
            if (string.IsNullOrWhiteSpace(i.Description)) errors.Add(new(f, "Descrição obrigatória."));
            if (string.IsNullOrWhiteSpace(i.Unit)) errors.Add(new(f, "Unidade obrigatória."));
            if (!IsDigits(i.Ncm, 8)) errors.Add(new(f, "NCM deve ter 8 dígitos."));
            if (!IsDigits(i.Cfop, 4)) errors.Add(new(f, "CFOP deve ter 4 dígitos."));
            else if (expectedCfopPrefix is not null && i.Cfop![0] != expectedCfopPrefix)
                errors.Add(new(f, $"CFOP {i.Cfop} incompatível com a operação: esperado iniciando em {expectedCfopPrefix} (origem {company.Address.State}, destino {customer?.Address.State})."));
            if (i.Origin is < 0 or > 8) errors.Add(new(f, "Origem da mercadoria deve ser de 0 a 8."));
            if (string.IsNullOrWhiteSpace(i.IcmsTaxCode)) errors.Add(new(f, "Informe o CST/CSOSN do ICMS."));
            else if (usesCsosn && !IsDigits(i.IcmsTaxCode, 3)) errors.Add(new(f, "Empresa do Simples Nacional (CRT 1/4) usa CSOSN com 3 dígitos."));
            else if (!usesCsosn && !IsDigits(i.IcmsTaxCode, 2)) errors.Add(new(f, "Empresa com CRT 2/3 usa CST do ICMS com 2 dígitos."));
            if (!string.IsNullOrWhiteSpace(i.Gtin) && i.Gtin != "SEM GTIN" && !IsDigits(i.Gtin, 8, 12, 13, 14))
                errors.Add(new(f, "GTIN deve ter 8, 12, 13 ou 14 dígitos (ou 'SEM GTIN')."));
            ValidateAmounts(i, f, errors);
        }
    }

    /// <summary>CFOP de saída: 5 operação interna, 6 interestadual, 7 exterior.</summary>
    public static char? ExpectedCfopPrefix(string? originState, string? destinationState)
    {
        if (string.IsNullOrWhiteSpace(originState) || string.IsNullOrWhiteSpace(destinationState)) return null;
        if (destinationState.Equals("EX", StringComparison.OrdinalIgnoreCase)) return '7';
        return destinationState.Equals(originState, StringComparison.OrdinalIgnoreCase) ? '5' : '6';
    }

    private static void ValidateNfse(FiscalDocument doc, DateTime now, List<ValidationError> errors)
    {
        if (doc.Items.Count != 1) errors.Add(new("Serviço", "A NFS-e (DPS) deve conter exatamente um serviço."));
        if (doc.Nfse.CompetenceDate is null) errors.Add(new("Competência", "Informe a data de competência."));
        else if (doc.Nfse.CompetenceDate > DateOnly.FromDateTime(now)) errors.Add(new("Competência", "Não pode ser futura."));
        if (!IsDigits(doc.Nfse.IncidenceMunicipalityIbge, 7)) errors.Add(new("Município de incidência", "Código IBGE deve ter 7 dígitos."));

        foreach (var i in doc.Items)
        {
            var f = $"Serviço ({i.Code})";
            if (!IsDigits(i.NationalTaxCode, 6)) errors.Add(new(f, "Código de tributação nacional deve ter 6 dígitos."));
            if (string.IsNullOrWhiteSpace(i.Description)) errors.Add(new(f, "Descrição (discriminação) obrigatória."));
            else if (i.Description.Length > 2000) errors.Add(new(f, "Descrição com mais de 2000 caracteres."));
            ValidateAmounts(i, f, errors);
        }
    }

    private static void ValidateAmounts(FiscalDocumentItem i, string f, List<ValidationError> errors)
    {
        if (i.Quantity <= 0) errors.Add(new(f, "Quantidade deve ser maior que zero."));
        if (i.UnitPrice <= 0) errors.Add(new(f, "Valor unitário deve ser maior que zero."));
        if (i.Discount < 0 || i.Discount >= i.GrossAmount && i.GrossAmount > 0) errors.Add(new(f, "Desconto inválido."));
        if (i.TaxRate is < 0 or > 100) errors.Add(new(f, "Alíquota deve estar entre 0 e 100."));
    }
}

/// <summary>Normalização e validação dos formulários de cadastro.</summary>
public static class CatalogValidator
{
    public static void Normalize(Address a)
    {
        a.ZipCode = NullIfEmpty(Digits(a.ZipCode));
        a.CityIbgeCode = NullIfEmpty(Digits(a.CityIbgeCode));
        a.State = a.State?.Trim().ToUpperInvariant();
    }

    public static List<ValidationError> Company(Company c)
    {
        c.Cnpj = Digits(c.Cnpj);
        Normalize(c.Address);
        var e = new List<ValidationError>();
        if (!IsValidCnpj(c.Cnpj)) e.Add(new(nameof(c.Cnpj), "CNPJ inválido."));
        if (string.IsNullOrWhiteSpace(c.LegalName)) e.Add(new(nameof(c.LegalName), "Razão social é obrigatória."));
        AddressErrors(c.Address, e);
        if (c.NfeSeries is < 0 or > 999) e.Add(new(nameof(c.NfeSeries), "Série da NF-e deve estar entre 0 e 999."));
        if (c.NfseSeries is < 1 or > 49999) e.Add(new(nameof(c.NfseSeries), "Série da NFS-e deve estar entre 1 e 49999."));
        if (c.NfeNextNumber < 1) e.Add(new(nameof(c.NfeNextNumber), "Deve ser maior que zero."));
        if (c.NfseNextNumber < 1) e.Add(new(nameof(c.NfseNextNumber), "Deve ser maior que zero."));
        return e;
    }

    public static List<ValidationError> Customer(Customer c)
    {
        c.Document = Digits(c.Document);
        Normalize(c.Address);
        var e = new List<ValidationError>();
        if (!IsValidCpfOrCnpj(c.Document)) e.Add(new(nameof(c.Document), "CPF/CNPJ inválido."));
        if (string.IsNullOrWhiteSpace(c.Name)) e.Add(new(nameof(c.Name), "Nome é obrigatório."));
        if (c.IeIndicator is not (1 or 2 or 9)) e.Add(new(nameof(c.IeIndicator), "Indicador inválido."));
        if (c.IeIndicator == 1 && string.IsNullOrWhiteSpace(c.StateRegistration))
            e.Add(new(nameof(c.StateRegistration), "Contribuinte do ICMS deve ter inscrição estadual."));
        AddressErrors(c.Address, e);
        return e;
    }

    public static List<ValidationError> Product(Product p)
    {
        p.Ncm = NullIfEmpty(Digits(p.Ncm));
        p.Cest = NullIfEmpty(Digits(p.Cest));
        p.Cfop = NullIfEmpty(Digits(p.Cfop));
        var e = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(p.Code)) e.Add(new(nameof(p.Code), "Código é obrigatório."));
        if (string.IsNullOrWhiteSpace(p.Description)) e.Add(new(nameof(p.Description), "Descrição é obrigatória."));
        if (string.IsNullOrWhiteSpace(p.Unit)) e.Add(new(nameof(p.Unit), "Unidade é obrigatória."));
        if (p.Ncm is not null && !IsDigits(p.Ncm, 8)) e.Add(new(nameof(p.Ncm), "NCM deve ter 8 dígitos."));
        if (p.Cest is not null && !IsDigits(p.Cest, 7)) e.Add(new(nameof(p.Cest), "CEST deve ter 7 dígitos."));
        if (p.Cfop is not null && !IsDigits(p.Cfop, 4)) e.Add(new(nameof(p.Cfop), "CFOP deve ter 4 dígitos."));
        if (p.Origin is < 0 or > 8) e.Add(new(nameof(p.Origin), "Origem de 0 a 8."));
        if (p.Price < 0) e.Add(new(nameof(p.Price), "Preço não pode ser negativo."));
        if (p.IcmsRate is < 0 or > 100) e.Add(new(nameof(p.IcmsRate), "Alíquota entre 0 e 100."));
        return e;
    }

    public static List<ValidationError> Service(Service s)
    {
        s.NationalTaxCode = NullIfEmpty(Digits(s.NationalTaxCode));
        s.IncidenceMunicipalityIbge = NullIfEmpty(Digits(s.IncidenceMunicipalityIbge));
        var e = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(s.Code)) e.Add(new(nameof(s.Code), "Código é obrigatório."));
        if (string.IsNullOrWhiteSpace(s.Description)) e.Add(new(nameof(s.Description), "Descrição é obrigatória."));
        if (s.NationalTaxCode is not null && !IsDigits(s.NationalTaxCode, 6))
            e.Add(new(nameof(s.NationalTaxCode), "Código de tributação nacional deve ter 6 dígitos."));
        if (s.IncidenceMunicipalityIbge is not null && !IsDigits(s.IncidenceMunicipalityIbge, 7))
            e.Add(new(nameof(s.IncidenceMunicipalityIbge), "Código IBGE deve ter 7 dígitos."));
        if (s.Price < 0) e.Add(new(nameof(s.Price), "Preço não pode ser negativo."));
        if (s.IssRate is < 0 or > 100) e.Add(new(nameof(s.IssRate), "Alíquota entre 0 e 100."));
        s.NbsCode = NullIfEmpty(Digits(s.NbsCode));
        if (s.NbsCode is not null && !IsDigits(s.NbsCode, 9)) e.Add(new(nameof(s.NbsCode), "Código NBS deve ter 9 dígitos."));
        s.MunicipalTaxCode = NullIfEmpty(Digits(s.MunicipalTaxCode));
        if (s.MunicipalTaxCode is not null && !IsDigits(s.MunicipalTaxCode, 3))
            e.Add(new(nameof(s.MunicipalTaxCode), "Código de tributação municipal deve ter 3 dígitos."));
        return e;
    }

    private static void AddressErrors(Address a, List<ValidationError> e)
    {
        if (a.ZipCode is not null && !IsDigits(a.ZipCode, 8)) e.Add(new("Address.ZipCode", "CEP deve ter 8 dígitos."));
        if (!string.IsNullOrEmpty(a.State) && a.State.Length != 2) e.Add(new("Address.State", "UF com 2 letras."));
        if (a.CityIbgeCode is not null && !IsDigits(a.CityIbgeCode, 7)) e.Add(new("Address.CityIbgeCode", "Código IBGE deve ter 7 dígitos."));
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
