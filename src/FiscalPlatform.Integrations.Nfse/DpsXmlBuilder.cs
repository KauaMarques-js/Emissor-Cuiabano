using System.Globalization;
using System.Text;
using System.Xml.Linq;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using static FiscalPlatform.Domain.DocumentNumbers;

namespace FiscalPlatform.Integrations.Nfse;

/// <summary>
/// Monta a DPS (Declaração de Prestação de Serviço) e o pedido de registro de evento no leiaute
/// v1.01 do Sistema Nacional NFS-e (esquemas NFSe-ESQUEMAS_XSD-v1.01-20260209, Anexo I e II).
/// </summary>
public static class DpsXmlBuilder
{
    public static readonly XNamespace Ns = "http://www.sped.fazenda.gov.br/nfse";
    public const string LayoutVersion = "1.01";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"DPS" + município emissor (7) + tipo de inscrição (1) + CNPJ/CPF (14) + série (5) + número (15).</summary>
    public static string DpsId(Company c, int series, long number)
    {
        var doc = Digits(c.Cnpj);
        var type = doc.Length == 11 ? "1" : "2";
        return "DPS" + Digits(c.Address.CityIbgeCode).PadLeft(7, '0') + type + doc.PadLeft(14, '0')
               + series.ToString("00000", Inv) + number.ToString("000000000000000", Inv);
    }

    public static int TpAmb(FiscalEnvironment env) => env == FiscalEnvironment.Producao ? 1 : 2;

    public static XDocument BuildDps(FiscalDocument d, Company c, string verAplic, DateTimeOffset now, string? substitutedKey)
    {
        if (d.Series is null || d.Number is null) throw new InvalidOperationException("Série e número da DPS não foram reservados.");
        var item = d.Items.Single();
        var opSimpNac = NfseRules.OpSimpNac(c);
        var regAp = NfseRules.RegApTribSN(c);

        var regTrib = new XElement(Ns + "regTrib", new XElement(Ns + "opSimpNac", opSimpNac));
        if (opSimpNac == 3 && regAp is not null) regTrib.Add(new XElement(Ns + "regApTribSN", regAp));
        regTrib.Add(new XElement(Ns + "regEspTrib", c.NfseSpecialRegime));

        // Quando o emitente é o próprio prestador, nome e endereço do prestador NÃO são informados.
        var prest = new XElement(Ns + "prest", Inscricao(c.Cnpj));
        if (!string.IsNullOrWhiteSpace(c.MunicipalRegistration)) prest.Add(new XElement(Ns + "IM", Clean(c.MunicipalRegistration, 15)));
        prest.Add(regTrib);

        var cServ = new XElement(Ns + "cServ", new XElement(Ns + "cTribNac", Digits(item.NationalTaxCode)));
        if (!string.IsNullOrEmpty(item.MunicipalTaxCode)) cServ.Add(new XElement(Ns + "cTribMun", Digits(item.MunicipalTaxCode)));
        cServ.Add(new XElement(Ns + "xDescServ", Clean(item.Description, 2000, allowNewLines: true)));
        if (!string.IsNullOrEmpty(item.NbsCode)) cServ.Add(new XElement(Ns + "cNBS", Digits(item.NbsCode)));

        var serv = new XElement(Ns + "serv",
            new XElement(Ns + "locPrest", new XElement(Ns + "cLocPrestacao", Digits(d.Nfse.IncidenceMunicipalityIbge))),
            cServ);
        if (!string.IsNullOrWhiteSpace(d.AdditionalInfo))
            serv.Add(new XElement(Ns + "infoCompl", new XElement(Ns + "xInfComp", Clean(d.AdditionalInfo, 2000))));

        var tribMun = new XElement(Ns + "tribMun",
            new XElement(Ns + "tribISSQN", 1),
            new XElement(Ns + "tpRetISSQN", d.Nfse.IssWithheld ? 2 : 1));
        if (NfseRules.InformsAliquot(c, d.Nfse.IssWithheld)) tribMun.Add(new XElement(Ns + "pAliq", Dec(item.TaxRate)));

        var valores = new XElement(Ns + "valores", new XElement(Ns + "vServPrest", new XElement(Ns + "vServ", Dec(item.GrossAmount))));
        if (item.Discount > 0)
            valores.Add(new XElement(Ns + "vDescCondIncond", new XElement(Ns + "vDescIncond", Dec(item.Discount))));
        valores.Add(new XElement(Ns + "trib", tribMun, TotTrib(c, opSimpNac)));

        var inf = new XElement(Ns + "infDPS",
            new XAttribute("Id", DpsId(c, d.Series.Value, d.Number.Value)),
            new XElement(Ns + "tpAmb", TpAmb(d.Environment)),
            new XElement(Ns + "dhEmi", DateTime(now)),
            new XElement(Ns + "verAplic", Clean(verAplic, 20)),
            new XElement(Ns + "serie", d.Series.Value.ToString(Inv)),
            new XElement(Ns + "nDPS", d.Number.Value.ToString(Inv)),
            new XElement(Ns + "dCompet", d.Nfse.CompetenceDate!.Value.ToString("yyyy-MM-dd", Inv)),
            new XElement(Ns + "tpEmit", 1),
            new XElement(Ns + "cLocEmi", Digits(c.Address.CityIbgeCode)));
        if (!string.IsNullOrEmpty(substitutedKey))
            inf.Add(new XElement(Ns + "subst",
                new XElement(Ns + "chSubstda", substitutedKey),
                new XElement(Ns + "cMotivo", "99"),
                new XElement(Ns + "xMotivo", "Substituição de NFS-e solicitada pelo prestador.")));
        inf.Add(prest);
        if (d.Customer is { } toma) inf.Add(Tomador(toma));
        inf.Add(serv, valores);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "DPS", new XAttribute("versao", LayoutVersion), inf));
    }

    /// <summary>Pedido de registro do evento 101101 (cancelamento de NFS-e).</summary>
    public static XDocument BuildCancellation(string accessKey, Company c, FiscalEnvironment env, string verAplic,
        DateTimeOffset now, string reasonCode, string reason)
    {
        const string eventType = "101101";
        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "pedRegEvento", new XAttribute("versao", LayoutVersion),
                new XElement(Ns + "infPedReg",
                    new XAttribute("Id", "PRE" + accessKey + eventType),
                    new XElement(Ns + "tpAmb", TpAmb(env)),
                    new XElement(Ns + "verAplic", Clean(verAplic, 20)),
                    new XElement(Ns + "dhEvento", DateTime(now)),
                    Digits(c.Cnpj).Length == 11
                        ? new XElement(Ns + "CPFAutor", Digits(c.Cnpj))
                        : new XElement(Ns + "CNPJAutor", Digits(c.Cnpj)),
                    new XElement(Ns + "chNFSe", accessKey),
                    new XElement(Ns + "e" + eventType,
                        new XElement(Ns + "xDesc", "Cancelamento de NFS-e"),
                        new XElement(Ns + "cMotivo", reasonCode is "1" or "2" or "9" ? reasonCode : "1"),
                        new XElement(Ns + "xMotivo", Clean(reason, 255))))));
    }

    private static XElement Tomador(Customer t)
    {
        var toma = new XElement(Ns + "toma", Inscricao(t.Document));
        if (!string.IsNullOrWhiteSpace(t.MunicipalRegistration)) toma.Add(new XElement(Ns + "IM", Clean(t.MunicipalRegistration, 15)));
        toma.Add(new XElement(Ns + "xNome", Clean(t.Name, 300)));

        var a = t.Address;
        // O endereço é opcional; só é enviado completo (cMun + CEP + logradouro + número + bairro).
        if (IsDigits(a.CityIbgeCode, 7) && IsDigits(a.ZipCode, 8) && !string.IsNullOrWhiteSpace(a.Street)
            && !string.IsNullOrWhiteSpace(a.Number) && !string.IsNullOrWhiteSpace(a.District))
        {
            var end = new XElement(Ns + "end",
                new XElement(Ns + "endNac", new XElement(Ns + "cMun", a.CityIbgeCode), new XElement(Ns + "CEP", a.ZipCode)),
                new XElement(Ns + "xLgr", Clean(a.Street, 255)),
                new XElement(Ns + "nro", Clean(a.Number, 60)));
            if (!string.IsNullOrWhiteSpace(a.Complement)) end.Add(new XElement(Ns + "xCpl", Clean(a.Complement, 156)));
            end.Add(new XElement(Ns + "xBairro", Clean(a.District, 60)));
            toma.Add(end);
        }
        var phone = Digits(t.Phone);
        if (phone.Length is >= 6 and <= 20) toma.Add(new XElement(Ns + "fone", phone));
        if (!string.IsNullOrWhiteSpace(t.Email) && t.Email.Trim().Length <= 80) toma.Add(new XElement(Ns + "email", Clean(t.Email, 80)));
        return toma;
    }

    private static XElement TotTrib(Company c, int opSimpNac) => new(Ns + "totTrib", opSimpNac switch
    {
        // MEI: opta por não informar valores estimados (Decreto 8.264/2014).
        2 => new XElement(Ns + "indTotTrib", 0),
        // ME/EPP: percentual aproximado do Simples Nacional.
        3 => new XElement(Ns + "pTotTribSN", Dec(c.NfseSimplesTaxPercent ?? 0)),
        // Não optante: percentuais aproximados por esfera.
        _ => new XElement(Ns + "pTotTrib",
            new XElement(Ns + "pTotTribFed", Dec(c.NfseFederalTaxPercent ?? 0)),
            new XElement(Ns + "pTotTribEst", Dec(0)),
            new XElement(Ns + "pTotTribMun", Dec(c.NfseMunicipalTaxPercent ?? 0))),
    });

    private static XElement Inscricao(string? document)
    {
        var digits = Digits(document);
        return new XElement(Ns + (digits.Length == 11 ? "CPF" : "CNPJ"), digits);
    }

    /// <summary>AAAA-MM-DDThh:mm:ssTZD. Um minuto no passado evita rejeição por relógio adiantado (dhEmi &gt; dhProc).</summary>
    private static string DateTime(DateTimeOffset now)
    {
        var t = now.AddMinutes(-1);
        t = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, t.Offset);
        return t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv);
    }

    private static string Dec(decimal v) => v == 0 ? "0.00" : Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv);

    /// <summary>Remove espaços extras e caracteres fora do conjunto aceito pelo leiaute (Latin-1, sem controle).</summary>
    public static string Clean(string? value, int maxLength, bool allowNewLines = false)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormC))
        {
            var c = ch switch
            {
                '–' or '—' or '−' => '-',
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '…' => '.',
                '\t' => ' ',
                _ => ch,
            };
            if (c is '\r') continue;
            if (c is '\n') { sb.Append(allowNewLines ? '\n' : ' '); continue; }
            if (c < ' ' || c > 'ÿ' || c is >= '\u007F' and <= '\u009F') continue;
            sb.Append(c);
        }
        var text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), allowNewLines ? "[ ]{2,}" : @"\s{2,}", " ").Trim();
        return text.Length > maxLength ? text[..maxLength].TrimEnd() : text;
    }
}
