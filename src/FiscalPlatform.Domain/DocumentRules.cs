namespace FiscalPlatform.Domain;

/// <summary>Quais ações são permitidas para um documento, conforme tipo e estado.</summary>
public static class DocumentRules
{
    public static bool CanEdit(FiscalDocument d) =>
        !d.Archived && d.Status is DocumentStatus.Rascunho or DocumentStatus.Rejeitada;

    public static bool CanEmit(FiscalDocument d) => CanEdit(d) && d.Items.Count > 0;

    /// <summary>Consulta é obrigatória antes de reenviar quando o resultado é desconhecido.</summary>
    public static bool CanQuery(FiscalDocument d) =>
        d.Status is DocumentStatus.Enviando or DocumentStatus.Pendente or DocumentStatus.FalhaComunicacao
            or DocumentStatus.Autorizada or DocumentStatus.Cancelada;

    public static bool MustQueryBeforeRetry(FiscalDocument d) =>
        d.Status is DocumentStatus.Enviando or DocumentStatus.Pendente or DocumentStatus.FalhaComunicacao;

    public static bool CanCancel(FiscalDocument d, DateTime now, int nfeCancelDeadlineHours)
    {
        if (d.Status != DocumentStatus.Autorizada) return false;
        if (d.Type == FiscalDocumentType.Nfe && d.AuthorizedAt is { } at)
            return now - at <= TimeSpan.FromHours(nfeCancelDeadlineHours);
        return true;
    }

    public static bool CanSendCorrectionLetter(FiscalDocument d) =>
        d.Type == FiscalDocumentType.Nfe && d.Status == DocumentStatus.Autorizada;

    public static bool CanSubstitute(FiscalDocument d) =>
        d.Type == FiscalDocumentType.Nfse && d.Status == DocumentStatus.Autorizada && d.SubstitutedByDocumentId is null;

    /// <summary>Só exclui rascunho que nunca reservou número (evita lacunas na numeração).</summary>
    public static bool CanDelete(FiscalDocument d) => d.Status == DocumentStatus.Rascunho && d.Number is null;

    public static bool CanArchive(FiscalDocument d) => !d.Archived && CanEdit(d) && !CanDelete(d);

    public static bool HasFiles(FiscalDocument d) => !string.IsNullOrEmpty(d.XmlPath);
}
