using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalPlatform.Integrations.Nfse;

/// <summary>Configuração da integração (seção "NfseNacional" do appsettings.json).</summary>
public sealed class NfseNacionalOptions
{
    /// <summary>Base da Sefin Nacional para homologação (produção restrita).</summary>
    public string HomologacaoBaseUrl { get; set; } = "https://sefin.producaorestrita.nfse.gov.br/SefinNacional";
    public string ProducaoBaseUrl { get; set; } = "https://sefin.nfse.gov.br/SefinNacional";
    /// <summary>Identificação do aplicativo na DPS (verAplic, até 20 caracteres).</summary>
    public string VerAplic { get; set; } = "EmissorFiscal-1.0";
    /// <summary>"sha256" (padrão) ou "sha1".</summary>
    public string SignatureAlgorithm { get; set; } = "sha256";
}

/// <summary>
/// Adaptador oficial da NFS-e pelo Sistema Nacional (Sefin Nacional), conforme o Manual dos Contribuintes
/// (Emissor Público API v1.2) e o leiaute v1.01: POST /nfse (DPS assinada, gzip + base64 em JSON),
/// GET /nfse/{chave}, GET /dps/{id} e POST /nfse/{chave}/eventos. Autenticação por mTLS com o A1 da empresa.
/// </summary>
public sealed class NfseNacionalProvider(ICertificateStore certificates, NfseNacionalOptions options,
    Func<X509Certificate2, HttpMessageHandler>? handlerFactory = null) : IFiscalProvider
{
    public FiscalDocumentType DocumentType => FiscalDocumentType.Nfse;
    public ProviderKind Kind => ProviderKind.Oficial;

    private bool UseSha1 => string.Equals(options.SignatureAlgorithm, "sha1", StringComparison.OrdinalIgnoreCase);

    public async Task<ProviderResult> TransmitAsync(ProviderRequest request, CancellationToken ct)
    {
        using var cert = await LoadCertificateAsync(request.Company.Id, ct);
        if (cert is null) return NoCertificate();

        var d = request.Document;
        string signedXml;
        try
        {
            var dps = DpsXmlBuilder.BuildDps(d, request.Company, options.VerAplic, DateTimeOffset.Now, request.SubstitutedAccessKey);
            signedXml = XmlSigner.Sign(dps, "infDPS", cert, UseSha1).OuterXml;
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException or System.Security.Cryptography.CryptographicException)
        {
            return new ProviderResult { Outcome = TransmissionOutcome.ValidacaoLocal, Message = "Falha ao montar/assinar a DPS: " + ex.Message };
        }

        var body = new JsonObject { ["dpsXmlGZipB64"] = GZipBase64(signedXml) }.ToJsonString();
        var call = await SendAsync(cert, HttpMethod.Post, BaseUrl(d) + "/nfse", body, ct);
        if (call.Failure is { } failure) return failure with { RequestPayload = signedXml };

        var (status, json, raw, _) = call;
        if (status is HttpStatusCode.OK or HttpStatusCode.Created && Str(json, "chaveAcesso") is { } key)
            return Authorized(json!, key, raw, signedXml);

        var errors = Errors(json);
        // E0014: já existe NFS-e gerada para esta série/número — a resposta anterior se perdeu. Recupera em vez de duplicar.
        if (errors.Any(e => e.Code == "E0014"))
        {
            var recovered = await QueryByDpsAsync(cert, request, ct);
            if (recovered.Outcome == TransmissionOutcome.Autorizada) return recovered with { RequestPayload = signedXml };
        }
        return Rejection(status, errors, raw, signedXml);
    }

    public async Task<ProviderResult> QueryAsync(ProviderRequest request, CancellationToken ct)
    {
        using var cert = await LoadCertificateAsync(request.Company.Id, ct);
        if (cert is null) return NoCertificate();
        var d = request.Document;
        if (string.IsNullOrEmpty(d.AccessKey)) return await QueryByDpsAsync(cert, request, ct);

        var call = await SendAsync(cert, HttpMethod.Get, BaseUrl(d) + "/nfse/" + d.AccessKey, null, ct);
        if (call.Failure is { } failure) return failure;
        if (call.Status == HttpStatusCode.OK) return Authorized(call.Json!, d.AccessKey, call.Raw, null);
        if (call.Status == HttpStatusCode.NotFound) return NotFound(call.Raw);
        return Rejection(call.Status, Errors(call.Json), call.Raw, null) with { Outcome = TransmissionOutcome.ErroInterno };
    }

    public async Task<ProviderResult> CancelAsync(ProviderRequest request, CancellationToken ct)
    {
        using var cert = await LoadCertificateAsync(request.Company.Id, ct);
        if (cert is null) return NoCertificate();
        var d = request.Document;
        if (string.IsNullOrEmpty(d.AccessKey))
            return new ProviderResult { Outcome = TransmissionOutcome.ValidacaoLocal, Message = "NFS-e sem chave de acesso; consulte a situação antes de cancelar." };

        var evt = DpsXmlBuilder.BuildCancellation(d.AccessKey, request.Company, d.Environment, options.VerAplic,
            DateTimeOffset.Now, request.EventCode ?? "1", request.EventText ?? "");
        var signedXml = XmlSigner.Sign(evt, "infPedReg", cert, UseSha1).OuterXml;
        var body = new JsonObject { ["pedidoRegistroEventoXmlGZipB64"] = GZipBase64(signedXml) }.ToJsonString();

        var call = await SendAsync(cert, HttpMethod.Post, $"{BaseUrl(d)}/nfse/{d.AccessKey}/eventos", body, ct);
        if (call.Failure is { } failure) return failure with { RequestPayload = signedXml };
        if (call.Status is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var eventXml = FirstXml(call.Json);
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.EventoRegistrado,
                Message = "Evento de cancelamento registrado no Sistema Nacional NFS-e.",
                Protocol = ProtocolFromEvent(eventXml),
                XmlContent = eventXml,
                RequestPayload = signedXml,
                ResponsePayload = call.Raw,
            };
        }
        return Rejection(call.Status, Errors(call.Json), call.Raw, signedXml) with { Outcome = TransmissionOutcome.EventoRejeitado };
    }

    public Task<ProviderResult> SendCorrectionLetterAsync(ProviderRequest request, CancellationToken ct) =>
        Task.FromResult(new ProviderResult { Outcome = TransmissionOutcome.EventoRejeitado, Message = "Carta de correção não se aplica à NFS-e." });

    /// <summary>GET /dps/{id}: descobre se a DPS gerou NFS-e (usado após timeout e na recuperação de E0014).</summary>
    private async Task<ProviderResult> QueryByDpsAsync(X509Certificate2 cert, ProviderRequest request, CancellationToken ct)
    {
        var d = request.Document;
        if (d.Series is null || d.Number is null) return NotFound(null);
        var dpsId = DpsXmlBuilder.DpsId(request.Company, d.Series.Value, d.Number.Value);
        var call = await SendAsync(cert, HttpMethod.Get, BaseUrl(d) + "/dps/" + dpsId, null, ct);
        if (call.Failure is { } failure) return failure;
        if (call.Status == HttpStatusCode.NotFound) return NotFound(call.Raw);
        if (call.Status != HttpStatusCode.OK || Str(call.Json, "chaveAcesso") is not { } key)
            return Rejection(call.Status, Errors(call.Json), call.Raw, null) with { Outcome = TransmissionOutcome.ErroInterno };

        var nfse = await SendAsync(cert, HttpMethod.Get, BaseUrl(d) + "/nfse/" + key, null, ct);
        if (nfse.Status == HttpStatusCode.OK && nfse.Json is not null) return Authorized(nfse.Json, key, nfse.Raw, null);
        // A chave existe mesmo sem conseguirmos baixar o XML agora.
        return new ProviderResult
        {
            Outcome = TransmissionOutcome.Autorizada, AccessKey = key, Message = "NFS-e gerada (chave recuperada pela DPS).",
            ResponsePayload = call.Raw,
        };
    }

    private ProviderResult Authorized(JsonNode json, string key, string? raw, string? request)
    {
        var xml = FirstXml(json);
        string? number = null;
        DateTime? processed = null;
        if (xml is not null)
        {
            try
            {
                var x = XDocument.Parse(xml);
                var inf = x.Descendants(DpsXmlBuilder.Ns + "infNFSe").FirstOrDefault();
                number = inf?.Element(DpsXmlBuilder.Ns + "nNFSe")?.Value;
                if (DateTimeOffset.TryParse(inf?.Element(DpsXmlBuilder.Ns + "dhProc")?.Value, out var dh)) processed = dh.LocalDateTime;
            }
            catch (XmlException) { }
        }
        if (processed is null && DateTimeOffset.TryParse(Str(json, "dataHoraProcessamento"), out var dhp)) processed = dhp.LocalDateTime;
        var alerts = Errors(json, "alertas");
        return new ProviderResult
        {
            Outcome = TransmissionOutcome.Autorizada,
            Code = "100",
            Message = "NFS-e gerada pelo Sistema Nacional." + (alerts.Count > 0 ? " Alertas: " + string.Join("; ", alerts) : ""),
            AccessKey = key,
            OfficialNumber = number,
            Protocol = key,
            AuthorizedAt = processed,
            XmlContent = xml,
            RequestPayload = request,
            ResponsePayload = Truncate(raw),
        };
    }

    private static ProviderResult Rejection(HttpStatusCode? status, List<ApiError> errors, string? raw, string? request) => new()
    {
        Outcome = status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? TransmissionOutcome.Rejeitada
            : TransmissionOutcome.ErroInterno,
        Code = errors.Count > 0 ? string.Join(",", errors.Select(e => e.Code).Distinct()) : $"HTTP {(int?)status}",
        Message = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : $"Resposta HTTP {(int?)status} sem detalhes de erro.",
        RequestPayload = request,
        ResponsePayload = Truncate(raw),
    };

    private static ProviderResult NotFound(string? raw) => new()
    {
        Outcome = TransmissionOutcome.NaoEncontrada,
        Message = "Nenhuma NFS-e foi gerada para esta DPS no Sistema Nacional.",
        ResponsePayload = Truncate(raw),
    };

    private static ProviderResult NoCertificate() => new()
    {
        Outcome = TransmissionOutcome.FalhaCertificado,
        Message = "Certificado A1 da empresa não encontrado ou não pôde ser aberto. Importe/renove o certificado.",
    };

    private string BaseUrl(FiscalDocument d) =>
        (d.Environment == FiscalEnvironment.Producao ? options.ProducaoBaseUrl : options.HomologacaoBaseUrl).TrimEnd('/');

    private async Task<X509Certificate2?> LoadCertificateAsync(int companyId, CancellationToken ct)
    {
        try { return await certificates.LoadActiveAsync(companyId, ct); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private sealed record ApiCall(HttpStatusCode? Status, JsonNode? Json, string? Raw, ProviderResult? Failure = null);

    private async Task<ApiCall> SendAsync(X509Certificate2 cert, HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        using var handler = handlerFactory?.Invoke(cert) ?? new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ClientCertificates = { cert },
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Accept.ParseAdd("application/json");
        if (jsonBody is not null) message.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex) when (IsConnectFailure(ex))
        {
            return new ApiCall(null, null, null, new ProviderResult
            {
                Outcome = TransmissionOutcome.Indisponivel,
                Message = "Não foi possível conectar ao Sistema Nacional NFS-e: " + ex.Message,
            });
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            return new ApiCall(null, null, null, new ProviderResult
            {
                Outcome = TransmissionOutcome.FalhaCertificado,
                Message = "Falha na conexão segura (TLS) com o certificado da empresa: " + ex.InnerException.Message,
            });
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new ApiCall(response.StatusCode, null, raw, new ProviderResult
                {
                    Outcome = TransmissionOutcome.FalhaCertificado,
                    Code = $"HTTP {(int)response.StatusCode}",
                    Message = "Acesso negado pelo Sistema Nacional NFS-e. Verifique se o certificado A1 é o da empresa e se ela está habilitada no Emissor Nacional.",
                    ResponsePayload = Truncate(raw),
                });
            JsonNode? json = null;
            try { json = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); }
            catch (JsonException) { }
            return new ApiCall(response.StatusCode, json, raw);
        }
    }

    private static bool IsConnectFailure(HttpRequestException ex) =>
        ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound or SocketError.NetworkUnreachable or SocketError.TryAgain }
        || ex.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError;

    private sealed record ApiError(string Code, string Description, string? Complement)
    {
        public override string ToString() => $"{Code} - {Description}" + (string.IsNullOrWhiteSpace(Complement) ? "" : $" ({Complement})");
    }

    /// <summary>Lê a lista de erros/alertas aceitando variações de maiúsculas ("Codigo"/"codigo").</summary>
    private static List<ApiError> Errors(JsonNode? json, string property = "erros")
    {
        var list = new List<ApiError>();
        if (json is not JsonObject obj) return list;
        var node = obj.FirstOrDefault(p => string.Equals(p.Key, property, StringComparison.OrdinalIgnoreCase)).Value
                   ?? (property == "erros" ? obj.FirstOrDefault(p => string.Equals(p.Key, "erro", StringComparison.OrdinalIgnoreCase)).Value : null);
        IEnumerable<JsonNode?> items = node switch
        {
            JsonArray array => array,
            JsonObject single => [single],
            _ => [],
        };
        foreach (var item in items.OfType<JsonObject>())
            list.Add(new ApiError(Str(item, "codigo") ?? "?", Str(item, "descricao") ?? Str(item, "mensagem") ?? item.ToJsonString(), Str(item, "complemento")));
        if (list.Count == 0 && property == "erros" && (Str(obj, "message") ?? Str(obj, "mensagem") ?? Str(obj, "title")) is { } msg)
            list.Add(new ApiError(Str(obj, "status") ?? "?", msg, Str(obj, "detail")));
        return list;
    }

    private static string? Str(JsonNode? json, string property)
    {
        if (json is not JsonObject obj) return null;
        var value = obj.FirstOrDefault(p => string.Equals(p.Key, property, StringComparison.OrdinalIgnoreCase)).Value;
        return value is JsonValue v ? v.ToString() : null;
    }

    /// <summary>Primeiro campo "*XmlGZipB64" da resposta (NFS-e ou evento), já descompactado.</summary>
    private static string? FirstXml(JsonNode? json)
    {
        if (json is not JsonObject obj) return null;
        var b64 = obj.Where(p => p.Key.EndsWith("XmlGZipB64", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (b64 is null) return null;
        try { return Gunzip(Convert.FromBase64String(b64)); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException) { return null; }
    }

    private static string? ProtocolFromEvent(string? xml)
    {
        if (xml is null) return null;
        try { return XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "infEvento")?.Attribute("Id")?.Value; }
        catch (XmlException) { return null; }
    }

    public static string GZipBase64(string xml)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(ms.ToArray());
    }

    public static string Gunzip(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? Truncate(string? s) => s is { Length: > 20000 } ? s[..20000] + "…" : s;
}

public static class NfseIntegration
{
    public static IServiceCollection AddNfseIntegration(this IServiceCollection services, NfseNacionalOptions? options = null)
    {
        services.AddSingleton(options ?? new NfseNacionalOptions());
        return services.AddScoped<IFiscalProvider>(sp =>
            new NfseNacionalProvider(sp.GetRequiredService<ICertificateStore>(), sp.GetRequiredService<NfseNacionalOptions>()));
    }
}
