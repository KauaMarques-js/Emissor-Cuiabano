using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Schema;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Integrations.Nfse;

namespace FiscalPlatform.UnitTests;

/// <summary>DPS e eventos validados contra os XSD oficiais v1.01 e cliente HTTP com respostas simuladas.</summary>
public class NfseNacionalTests
{
    private static readonly string SchemaDir = Path.Combine(AppContext.BaseDirectory, "Schemas", "nfse-1.01");
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 30, 0, TimeSpan.FromHours(-4));

    private static Company Company(TaxRegime regime = TaxRegime.SimplesNacional)
    {
        var c = TestData.Company();
        c.TaxRegime = regime;
        c.NfseProvider = ProviderKind.Oficial;
        c.NfseSimplesTaxPercent = 6m;
        c.NfseFederalTaxPercent = 13.45m;
        c.NfseMunicipalTaxPercent = 3m;
        return c;
    }

    private static FiscalDocument Document(Company c, bool withheld = false, decimal discount = 0)
    {
        var d = TestData.NfseDraft(c.Id, 1);
        d.Customer = TestData.Customer(c.Id);
        d.Customer.Address.ZipCode = "78005000";
        d.Customer.Email = "cliente@exemplo.com.br";
        d.Customer.Phone = "(65) 99999-0000";
        d.Environment = FiscalEnvironment.Homologacao;
        d.Series = 1;
        d.Number = 123;
        d.Nfse.IssWithheld = withheld;
        d.AdditionalInfo = "Contrato nº 45 — referente a outubro";
        d.Items[0].NbsCode = "115022000";
        d.Items[0].Discount = discount;
        d.Recalculate();
        return d;
    }

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=EMPRESA TESTE:11222333000181", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "x"), "x");
    }

    private static bool VerifySignature(XmlDocument xml)
    {
        var signed = new SignedXml(xml);
        signed.LoadXml(xml.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).OfType<XmlElement>().Single());
        return signed.CheckSignature();
    }

    private static void ValidateAgainstSchema(XmlDocument xml, string schemaFile)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        using (var reader = XmlReader.Create(Path.Combine(SchemaDir, schemaFile), settings)) schemas.Add(null, reader);
        var errors = new List<string>();
        xml.Schemas = schemas;
        xml.Validate((_, e) => errors.Add(e.Message));
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors) + Environment.NewLine + xml.OuterXml);
    }

    [Theory]
    [InlineData(TaxRegime.SimplesNacional, false)]
    [InlineData(TaxRegime.SimplesNacional, true)]
    [InlineData(TaxRegime.SimplesNacionalExcesso, false)]
    [InlineData(TaxRegime.Mei, false)]
    [InlineData(TaxRegime.RegimeNormal, true)]
    public void Signed_dps_is_valid_against_official_schema(TaxRegime regime, bool withheld)
    {
        var c = Company(regime);
        var d = Document(c, withheld, discount: 50);
        using var cert = Certificate();

        var xml = XmlSigner.Sign(DpsXmlBuilder.BuildDps(d, c, "EmissorFiscal-1.0", Now, null), "infDPS", cert);

        ValidateAgainstSchema(xml, "DPS_v1.01.xsd");
        Assert.True(VerifySignature(xml));
    }

    [Fact]
    public void Schema_validation_detects_invalid_dps()
    {
        var c = Company();
        var dps = DpsXmlBuilder.BuildDps(Document(c), c, "v", Now, null);
        dps.Descendants(DpsXmlBuilder.Ns + "cTribNac").Single().Value = "123";
        var xml = new XmlDocument();
        xml.LoadXml(dps.ToString());
        Assert.ThrowsAny<Exception>(() => ValidateAgainstSchema(xml, "DPS_v1.01.xsd"));
    }

    [Fact]
    public void Dps_follows_business_rules_of_annex_I()
    {
        var c = Company();
        var d = Document(c, withheld: true);
        var x = DpsXmlBuilder.BuildDps(d, c, "EmissorFiscal-1.0", Now, new string('4', 50));
        var ns = DpsXmlBuilder.Ns;
        var inf = x.Root!.Element(ns + "infDPS")!;

        Assert.Equal("DPS510340321122233300018100001000000000000123", inf.Attribute("Id")!.Value);
        Assert.Equal(45, inf.Attribute("Id")!.Value.Length);
        Assert.Equal("2026-10-24T10:29:00-04:00", inf.Element(ns + "dhEmi")!.Value);
        Assert.Null(inf.Element(ns + "prest")!.Element(ns + "xNome")); // emitente = prestador: sem nome/endereço
        Assert.Null(inf.Element(ns + "prest")!.Element(ns + "end"));
        Assert.Equal("3", inf.Descendants(ns + "opSimpNac").Single().Value);
        Assert.Equal("1", inf.Descendants(ns + "regApTribSN").Single().Value);
        Assert.Equal("2.00", inf.Descendants(ns + "pAliq").Single().Value); // ME/EPP com ISS retido informa alíquota
        Assert.Equal("6.00", inf.Descendants(ns + "pTotTribSN").Single().Value);
        Assert.Contains("-", inf.Descendants(ns + "xInfComp").Single().Value); // travessão convertido
        Assert.NotNull(inf.Element(ns + "subst"));
    }

    [Fact]
    public void Aliquot_is_omitted_when_not_allowed()
    {
        foreach (var (regime, withheld) in new[] { (TaxRegime.SimplesNacional, false), (TaxRegime.RegimeNormal, true), (TaxRegime.Mei, true) })
        {
            var c = Company(regime);
            var x = DpsXmlBuilder.BuildDps(Document(c, withheld), c, "v", Now, null);
            Assert.Empty(x.Descendants(DpsXmlBuilder.Ns + "pAliq"));
        }
    }

    [Fact]
    public void Cancellation_event_is_valid_against_official_schema()
    {
        var c = Company();
        var key = new string('1', 50);
        using var cert = Certificate();
        var xml = XmlSigner.Sign(DpsXmlBuilder.BuildCancellation(key, c, FiscalEnvironment.Homologacao, "v1", Now, "2",
            "Serviço não foi prestado ao cliente"), "infPedReg", cert);

        ValidateAgainstSchema(xml, "pedRegEvento_v1.01.xsd");
        Assert.True(VerifySignature(xml));
        Assert.Equal("PRE" + key + "101101", xml.GetElementsByTagName("infPedReg")[0]!.Attributes!["Id"]!.Value);
    }

    [Fact]
    public void Company_rules_require_simples_percent_and_series_range()
    {
        var c = Company();
        c.NfseSimplesTaxPercent = null;
        c.NfseSeries = 70000;
        Assert.Contains(NfseRules.CompanyProblems(c), p => p.Contains("Simples Nacional"));
        Assert.Contains(CatalogValidator.Company(c), e => e.Message.Contains("49999"));
    }

    // ---------- cliente HTTP ----------

    private sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body));
            return respond(request, body);
        }
    }

    private sealed class FakeCertificates : ICertificateStore
    {
        public Task<CertificateImportResult> ImportAsync(int companyId, Stream pfx, string password, string user, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<X509Certificate2?> LoadActiveAsync(int companyId, CancellationToken ct = default) => Task.FromResult<X509Certificate2?>(Certificate());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static string NfseXml(string key) =>
        $"""<NFSe xmlns="http://www.sped.fazenda.gov.br/nfse" versao="1.01"><infNFSe Id="NFS{key}"><nNFSe>77</nNFSe><dhProc>2026-10-24T10:31:00-04:00</dhProc></infNFSe></NFSe>""";

    private static (NfseNacionalProvider Provider, FakeHandler Handler) Provider(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new NfseNacionalProvider(new FakeCertificates(), new NfseNacionalOptions(), _ => handler), handler);
    }

    private static ProviderRequest Request(FiscalDocument d, Company c) => new(d, c, TransmissionOperation.Emissao, Guid.NewGuid());

    [Fact]
    public async Task Authorized_response_is_parsed()
    {
        var key = "5103403211222333000181000000000000771234567890";
        key = key.PadRight(50, '0');
        var (provider, handler) = Provider((_, _) => Json(HttpStatusCode.Created, new JsonObject
        {
            ["tipoAmbiente"] = 2, ["chaveAcesso"] = key, ["idDps"] = "x",
            ["nfseXmlGZipB64"] = NfseNacionalProvider.GZipBase64(NfseXml(key)),
        }));
        var c = Company();
        var result = await provider.TransmitAsync(Request(Document(c), c), CancellationToken.None);

        Assert.Equal(TransmissionOutcome.Autorizada, result.Outcome);
        Assert.Equal(key, result.AccessKey);
        Assert.Equal("77", result.OfficialNumber);
        Assert.Contains("<NFSe", result.XmlContent);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://sefin.producaorestrita.nfse.gov.br/SefinNacional/nfse", call.Url);
        var sent = NfseNacionalProvider.Gunzip(Convert.FromBase64String(JsonNode.Parse(call.Body!)!["dpsXmlGZipB64"]!.GetValue<string>()));
        Assert.Contains("<Signature", sent);
        Assert.StartsWith("<?xml", sent);
    }

    [Fact]
    public async Task Rejection_keeps_official_codes_and_messages()
    {
        var (provider, _) = Provider((_, _) => Json(HttpStatusCode.BadRequest, new JsonObject
        {
            ["erros"] = new JsonArray(
                new JsonObject { ["Codigo"] = "E0116", ["Descricao"] = "IM não informada" },
                new JsonObject { ["codigo"] = "E0322", ["descricao"] = "NBS inválido", ["complemento"] = "cNBS" }),
        }));
        var c = Company();
        var result = await provider.TransmitAsync(Request(Document(c), c), CancellationToken.None);

        Assert.Equal(TransmissionOutcome.Rejeitada, result.Outcome);
        Assert.Equal("E0116,E0322", result.Code);
        Assert.Contains("E0116 - IM não informada", result.Message);
        Assert.Contains("E0322 - NBS inválido (cNBS)", result.Message);
    }

    [Fact]
    public async Task Duplicate_dps_recovers_existing_nfse_instead_of_rejecting()
    {
        var key = new string('9', 50);
        var (provider, handler) = Provider((req, _) => req.Method == HttpMethod.Post
            ? Json(HttpStatusCode.BadRequest, new JsonObject { ["erros"] = new JsonArray(new JsonObject { ["Codigo"] = "E0014", ["Descricao"] = "DPS já existe" }) })
            : req.RequestUri!.AbsolutePath.Contains("/dps/")
                ? Json(HttpStatusCode.OK, new JsonObject { ["chaveAcesso"] = key })
                : Json(HttpStatusCode.OK, new JsonObject { ["chaveAcesso"] = key, ["nfseXmlGZipB64"] = NfseNacionalProvider.GZipBase64(NfseXml(key)) }));
        var c = Company();
        var result = await provider.TransmitAsync(Request(Document(c), c), CancellationToken.None);

        Assert.Equal(TransmissionOutcome.Autorizada, result.Outcome);
        Assert.Equal(key, result.AccessKey);
        Assert.Contains(handler.Calls, x => x.Url.EndsWith("/dps/DPS510340321122233300018100001000000000000123"));
    }

    [Fact]
    public async Task Server_error_means_unknown_result_and_query_not_found_releases()
    {
        var (provider, _) = Provider((req, _) => req.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var c = Company();
        var d = Document(c);

        Assert.Equal(TransmissionOutcome.ErroInterno, (await provider.TransmitAsync(Request(d, c), CancellationToken.None)).Outcome);
        Assert.Equal(TransmissionOutcome.NaoEncontrada, (await provider.QueryAsync(Request(d, c), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Forbidden_is_reported_as_certificate_problem()
    {
        var (provider, _) = Provider((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var c = Company();
        var result = await provider.TransmitAsync(Request(Document(c), c), CancellationToken.None);
        Assert.Equal(TransmissionOutcome.FalhaCertificado, result.Outcome);
    }

    [Fact]
    public async Task Cancellation_posts_signed_event()
    {
        var key = new string('3', 50);
        var (provider, handler) = Provider((_, _) => Json(HttpStatusCode.Created, new JsonObject
        {
            ["eventoXmlGZipB64"] = NfseNacionalProvider.GZipBase64($"""<evento xmlns="http://www.sped.fazenda.gov.br/nfse"><infEvento Id="EVT{key}101101001"/></evento>"""),
        }));
        var c = Company();
        var d = Document(c);
        d.AccessKey = key;
        var result = await provider.CancelAsync(new ProviderRequest(d, c, TransmissionOperation.Cancelamento, Guid.NewGuid(),
            "Erro no valor do serviço prestado") { EventCode = "1" }, CancellationToken.None);

        Assert.Equal(TransmissionOutcome.EventoRegistrado, result.Outcome);
        Assert.Equal($"EVT{key}101101001", result.Protocol);
        Assert.EndsWith($"/nfse/{key}/eventos", handler.Calls.Single().Url);
    }
}
