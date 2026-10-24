using System.Security;
using System.Text.Json;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// Provedor local para testar o fluxo completo SEM contato com serviços oficiais. Tudo o que ele devolve é
/// marcado como simulação e não tem valor fiscal. Códigos "SIM-*" não são códigos oficiais.
/// Marcadores em "Informações complementares" forçam cenários: [SIM:REJEITAR], [SIM:TIMEOUT],
/// [SIM:PENDENTE], [SIM:INDISPONIVEL].
/// </summary>
public sealed class SimulatedFiscalProvider(FiscalDocumentType documentType) : IFiscalProvider
{
    public FiscalDocumentType DocumentType => documentType;
    public ProviderKind Kind => ProviderKind.Simulacao;

    public async Task<ProviderResult> TransmitAsync(ProviderRequest r, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(200, 700), ct);
        var request = RequestPayload(r);
        var info = r.Document.AdditionalInfo ?? "";

        if (info.Contains("[SIM:REJEITAR]", StringComparison.OrdinalIgnoreCase))
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.Rejeitada,
                Code = "SIM-999",
                Message = "Rejeição simulada: o campo Informações complementares contém o marcador [SIM:REJEITAR]. (Mensagem gerada pela simulação, não é oficial.)",
                Details = "Campo apontado: AdditionalInfo",
                RequestPayload = request,
                ResponsePayload = "{\"simulacao\":true,\"status\":\"rejeitada\",\"codigo\":\"SIM-999\"}",
            };
        if (info.Contains("[SIM:TIMEOUT]", StringComparison.OrdinalIgnoreCase))
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.Timeout,
                Message = "Timeout simulado: o envio ocorreu, mas a resposta não chegou. Consulte a situação.",
                RequestPayload = request,
            };
        if (info.Contains("[SIM:INDISPONIVEL]", StringComparison.OrdinalIgnoreCase))
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.Indisponivel,
                Code = "SIM-503",
                Message = "Serviço indisponível (simulação). Nada foi enviado.",
            };
        if (info.Contains("[SIM:PENDENTE]", StringComparison.OrdinalIgnoreCase))
            return new ProviderResult
            {
                Outcome = TransmissionOutcome.EmProcessamento,
                Code = "SIM-105",
                Message = "Lote em processamento (simulação). Consulte a situação.",
                Protocol = "SIMREC" + r.OperationId.ToString("N")[..10],
                RequestPayload = request,
            };
        return Authorized(r, request);
    }

    public async Task<ProviderResult> QueryAsync(ProviderRequest r, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);
        var d = r.Document;
        if (d.Status == DocumentStatus.Cancelada)
            return new ProviderResult { Outcome = TransmissionOutcome.EventoRegistrado, Message = "Documento consta como cancelado (simulação).", Protocol = d.CancelProtocol };
        if (d.AccessKey is not null)
            return new ProviderResult { Outcome = TransmissionOutcome.Autorizada, Message = "Documento consta como autorizado (simulação).", Protocol = d.Protocol, AccessKey = d.AccessKey };
        var info = d.AdditionalInfo ?? "";
        // Simula o caso em que o envio "perdido" foi, na verdade, processado pelo serviço.
        if (info.Contains("[SIM:TIMEOUT]", StringComparison.OrdinalIgnoreCase) || info.Contains("[SIM:PENDENTE]", StringComparison.OrdinalIgnoreCase))
            return Authorized(r, null);
        return new ProviderResult { Outcome = TransmissionOutcome.NaoEncontrada, Code = "SIM-404", Message = "Documento não encontrado no serviço (simulação)." };
    }

    public Task<ProviderResult> CancelAsync(ProviderRequest r, CancellationToken ct) =>
        Task.FromResult(Event(r, "cancelamento"));

    public Task<ProviderResult> SendCorrectionLetterAsync(ProviderRequest r, CancellationToken ct) =>
        Task.FromResult(documentType == FiscalDocumentType.Nfe
            ? Event(r, "carta-correcao")
            : new ProviderResult { Outcome = TransmissionOutcome.EventoRejeitado, Message = "Carta de correção não se aplica à NFS-e." });

    private ProviderResult Authorized(ProviderRequest r, string? request)
    {
        var d = r.Document;
        var now = DateTime.Now;
        var protocol = "SIM" + now.ToString("yyMMddHHmmss") + Random.Shared.Next(1000, 9999);
        string accessKey;
        string? verification = null;
        if (documentType == FiscalDocumentType.Nfe)
        {
            var baseKey = $"51{now:yyMM}{r.Company.Cnpj}55{d.Series ?? 0:000}{d.Number ?? 0:000000000}1{Random.Shared.Next(10000000, 99999999)}";
            accessKey = baseKey + Mod11(baseKey);
        }
        else
        {
            accessKey = $"SIMNFSE{r.Company.Cnpj}{d.Number ?? 0:000000000}";
            verification = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- SIMULAÇÃO: documento sem valor fiscal, gerado localmente pela plataforma. -->
            <Simulacao tipo="{documentType}" ambiente="{d.Environment}">
              <Chave>{accessKey}</Chave>
              <Protocolo>{protocol}</Protocolo>
              <Emitente cnpj="{r.Company.Cnpj}">{SecurityElement.Escape(r.Company.LegalName)}</Emitente>
              <Numero serie="{d.Series}">{d.Number}</Numero>
              <ValorTotal>{d.TotalAmount:0.00}</ValorTotal>
              <DataAutorizacao>{now:O}</DataAutorizacao>
            </Simulacao>
            """;
        return new ProviderResult
        {
            Outcome = TransmissionOutcome.Autorizada,
            Code = "SIM-100",
            Message = "Autorizado (SIMULAÇÃO — sem valor fiscal).",
            Protocol = protocol,
            AccessKey = accessKey,
            OfficialNumber = d.Number?.ToString(),
            VerificationCode = verification,
            AuthorizedAt = now,
            RequestPayload = request,
            ResponsePayload = JsonSerializer.Serialize(new { simulacao = true, status = "autorizada", chave = accessKey, protocolo = protocol }),
            XmlContent = xml,
        };
    }

    private static ProviderResult Event(ProviderRequest r, string kind)
    {
        var protocol = "SIMEVT" + DateTime.Now.ToString("yyMMddHHmmss");
        return new ProviderResult
        {
            Outcome = TransmissionOutcome.EventoRegistrado,
            Code = "SIM-135",
            Message = $"Evento de {kind} registrado (SIMULAÇÃO — sem valor fiscal).",
            Protocol = protocol,
            RequestPayload = JsonSerializer.Serialize(new { evento = kind, texto = r.EventText, sequencia = r.EventSequence, chave = r.Document.AccessKey }),
            XmlContent = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- SIMULAÇÃO -->\n<EventoSimulado tipo=\"{kind}\" protocolo=\"{protocol}\" chave=\"{r.Document.AccessKey}\"><Texto>{SecurityElement.Escape(r.EventText)}</Texto></EventoSimulado>",
        };
    }

    private static string RequestPayload(ProviderRequest r) => JsonSerializer.Serialize(new
    {
        simulacao = true,
        r.OperationId,
        tipo = r.Document.Type.ToString(),
        r.Document.Series,
        r.Document.Number,
        emitente = r.Company.Cnpj,
        r.Document.TotalAmount,
        itens = r.Document.Items.Select(i => new { i.Code, i.Description, i.Quantity, i.UnitPrice, i.Total }),
    });

    private static int Mod11(string digits)
    {
        int sum = 0, weight = 2;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
