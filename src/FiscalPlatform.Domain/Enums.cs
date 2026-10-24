using System.ComponentModel.DataAnnotations;

namespace FiscalPlatform.Domain;

public enum FiscalDocumentType
{
    [Display(Name = "NF-e 55")] Nfe = 1,
    [Display(Name = "NFS-e")] Nfse = 2,
}

public enum FiscalEnvironment
{
    [Display(Name = "Produção")] Producao = 1,
    [Display(Name = "Homologação")] Homologacao = 2,
}

/// <summary>Quem atende a transmissão: simulação local (sem valor fiscal) ou integração oficial.</summary>
public enum ProviderKind
{
    [Display(Name = "Simulação (sem valor fiscal)")] Simulacao = 0,
    [Display(Name = "Integração oficial")] Oficial = 1,
}

/// <summary>Como as notas de um tipo são emitidas (combina provedor e ambiente em uma escolha simples).</summary>
public enum EmissionMode
{
    [Display(Name = "Simulação (não envia nada)")] Simulacao = 0,
    [Display(Name = "Teste no governo (homologação)")] Homologacao = 1,
    [Display(Name = "Valendo (produção)")] Producao = 2,
}

public enum DocumentStatus
{
    [Display(Name = "Rascunho")] Rascunho = 0,
    [Display(Name = "Enviando")] Enviando = 1,
    [Display(Name = "Pendente")] Pendente = 2,
    [Display(Name = "Autorizada")] Autorizada = 3,
    [Display(Name = "Rejeitada")] Rejeitada = 4,
    [Display(Name = "Cancelada")] Cancelada = 5,
    [Display(Name = "Falha de comunicação")] FalhaComunicacao = 6,
}

public enum TaxRegime
{
    [Display(Name = "Não informado")] NaoInformado = 0,
    [Display(Name = "MEI")] Mei = 1,
    [Display(Name = "Simples Nacional")] SimplesNacional = 2,
    [Display(Name = "Simples Nacional - excesso de sublimite")] SimplesNacionalExcesso = 3,
    [Display(Name = "Regime Normal (Lucro Presumido/Real)")] RegimeNormal = 4,
}

public enum TransmissionOperation
{
    [Display(Name = "Emissão")] Emissao = 0,
    [Display(Name = "Consulta")] Consulta = 1,
    [Display(Name = "Cancelamento")] Cancelamento = 2,
    [Display(Name = "Carta de correção")] CartaCorrecao = 3,
}

public enum TransmissionOutcome
{
    [Display(Name = "Autorizada")] Autorizada = 0,
    [Display(Name = "Rejeitada (oficial)")] Rejeitada = 1,
    [Display(Name = "Em processamento")] EmProcessamento = 2,
    [Display(Name = "Não encontrada na consulta")] NaoEncontrada = 3,
    [Display(Name = "Evento registrado")] EventoRegistrado = 4,
    [Display(Name = "Evento rejeitado")] EventoRejeitado = 5,
    [Display(Name = "Falha de validação local")] ValidacaoLocal = 6,
    [Display(Name = "Falha de certificado/autenticação")] FalhaCertificado = 8,
    [Display(Name = "Serviço indisponível")] Indisponivel = 9,
    [Display(Name = "Timeout (resultado desconhecido)")] Timeout = 10,
    [Display(Name = "Erro interno (resultado desconhecido)")] ErroInterno = 11,
}

public enum CertificateStatus
{
    [Display(Name = "Não testado")] NaoTestado,
    [Display(Name = "Válido")] Valido,
    [Display(Name = "Próximo do vencimento")] ProximoVencimento,
    [Display(Name = "Expirado")] Expirado,
    [Display(Name = "Inválido para esta empresa")] Invalido,
}

public enum BatchStatus
{
    [Display(Name = "Processando")] Processando = 0,
    [Display(Name = "Concluído")] Concluido = 1,
}

public enum BatchItemStatus
{
    [Display(Name = "Aguardando")] Aguardando = 0,
    [Display(Name = "Processando")] Processando = 1,
    [Display(Name = "Autorizada")] Autorizada = 2,
    [Display(Name = "Rejeitada")] Rejeitada = 3,
    [Display(Name = "Pendente de consulta")] PendenteConsulta = 4,
    [Display(Name = "Falha de comunicação")] FalhaComunicacao = 5,
    [Display(Name = "Não enviado")] NaoEnviado = 6,
}

public static class EnumText
{
    /// <summary>Nome de exibição definido no atributo [Display] do valor.</summary>
    public static string Of(Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttributes(typeof(DisplayAttribute), false)
            .OfType<DisplayAttribute>().FirstOrDefault()?.GetName() ?? value.ToString();
}
