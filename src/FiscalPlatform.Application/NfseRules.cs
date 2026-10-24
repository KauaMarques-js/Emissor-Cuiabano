using FiscalPlatform.Domain;

namespace FiscalPlatform.Application;

/// <summary>
/// Regras da DPS do Sistema Nacional NFS-e (Anexo I, leiaute v1.01) que dependem do cadastro da empresa.
/// Conferir com o contador; o serviço oficial continua sendo a validação definitiva.
/// </summary>
public static class NfseRules
{
    /// <summary>opSimpNac: 1 não optante, 2 MEI, 3 ME/EPP.</summary>
    public static int OpSimpNac(Company c) => c.TaxRegime switch
    {
        TaxRegime.Mei => 2,
        TaxRegime.SimplesNacional or TaxRegime.SimplesNacionalExcesso => 3,
        _ => 1,
    };

    /// <summary>regApTribSN (somente ME/EPP): 1 tudo pelo SN; 2 ISSQN fora do SN (excesso de sublimite).</summary>
    public static int? RegApTribSN(Company c) => c.TaxRegime switch
    {
        TaxRegime.SimplesNacional => 1,
        TaxRegime.SimplesNacionalExcesso => 2,
        _ => null,
    };

    /// <summary>
    /// A alíquota (pAliq) só é informada pelo emitente quando o prestador é ME/EPP com ISSQN apurado pelo SN e há retenção;
    /// nos demais casos com município ativo no Sistema Nacional ela é proibida (o sistema usa a parametrizada).
    /// </summary>
    public static bool InformsAliquot(Company c, bool issWithheld) =>
        OpSimpNac(c) == 3 && RegApTribSN(c) == 1 && issWithheld;

    public static IEnumerable<string> CompanyProblems(Company c)
    {
        if (c.NfseSpecialRegime is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 9))
            yield return "Regime especial de tributação inválido.";
        if (c.TaxRegime == TaxRegime.SimplesNacional && c.NfseSpecialRegime != 0)
            yield return "Optante do Simples Nacional (ME/EPP) deve usar regime especial \"Nenhum\".";
        switch (OpSimpNac(c))
        {
            case 3 when c.NfseSimplesTaxPercent is null or < 0 or > 99.99m:
                yield return "Informe o percentual aproximado de tributos do Simples Nacional (campo exigido na NFS-e para ME/EPP).";
                break;
            case 1 when c.NfseFederalTaxPercent is null || c.NfseMunicipalTaxPercent is null:
                yield return "Informe os percentuais aproximados de tributos federais e municipais (Lei 12.741/2012).";
                break;
        }
    }

    public static IEnumerable<ValidationError> DocumentProblems(FiscalDocument doc, Company c) =>
        InformsAliquot(c, doc.Nfse.IssWithheld)
            ? doc.Items.Where(i => i.TaxRate is <= 0 or > 5).Select(i => new ValidationError($"Serviço ({i.Code})",
                "Com ISS retido, ME/EPP deve informar a alíquota do ISS (até 5%) no cadastro do serviço."))
            : [];
}
