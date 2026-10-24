using System.Globalization;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// PDF simplificado da nota (dados, itens, totais, chave e protocolo). Não é o DANFE/DANFSe oficial:
/// o leiaute oficial (com código de barras/QR Code) deve ser gerado conforme o manual quando a integração existir.
/// </summary>
public sealed class DocumentPdfRenderer : IDocumentPdfRenderer
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const double Margin = 36;

    static DocumentPdfRenderer()
    {
        if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public byte[] Render(FiscalDocument d, Company company)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{TypeLabel(d)} {d.Series}-{d.Number}";
        var writer = new PageWriter(pdf);

        if (d.IsSimulated)
            writer.Banner("SIMULAÇÃO — SEM VALOR FISCAL", XColors.DarkRed);
        else if (d.Environment == FiscalEnvironment.Homologacao)
            writer.Banner($"{TypeLabel(d)} EMITIDA EM HOMOLOGAÇÃO — SEM VALIDADE JURÍDICA", XColors.DarkRed);
        if (d.Status == DocumentStatus.Cancelada)
            writer.Banner($"NOTA CANCELADA em {d.CancelledAt:dd/MM/yyyy HH:mm} — {d.CancelReason}", XColors.DarkRed);
        else if (d.Status != DocumentStatus.Autorizada)
            writer.Banner($"NOTA NÃO AUTORIZADA — situação: {EnumText.Of(d.Status)}", XColors.DarkOrange);

        writer.Title($"{TypeLabel(d)}  —  Série {d.Series}  Nº {d.Number}");
        writer.Small("Documento auxiliar simplificado gerado pela plataforma. Não substitui o DANFE/DANFSe oficial.");
        writer.Space(6);

        writer.Box("EMITENTE", [
            company.LegalName,
            $"CNPJ {DocumentNumbers.Format(company.Cnpj)}   IE {company.StateRegistration}   IM {company.MunicipalRegistration}",
            Address(company.Address),
        ]);

        var c = d.Customer;
        writer.Box(d.Type == FiscalDocumentType.Nfe ? "DESTINATÁRIO" : "TOMADOR", c is null ? ["—"] : [
            c.Name,
            $"CPF/CNPJ {DocumentNumbers.Format(c.Document)}   {(string.IsNullOrEmpty(c.StateRegistration) ? "" : "IE " + c.StateRegistration)}",
            Address(c.Address),
        ]);

        var official = new List<string>
        {
            $"Chave/identificador: {GroupKey(d.AccessKey)}",
            $"Protocolo: {d.Protocol}   Autorizada em: {d.AuthorizedAt:dd/MM/yyyy HH:mm}   Ambiente: {EnumText.Of(d.Environment)}",
        };
        if (d.Type == FiscalDocumentType.Nfe) official.Add($"Natureza da operação: {d.Nfe.NatureOfOperation}");
        else official.Add($"Nº NFS-e: {d.OfficialNumber}   Código de verificação: {d.VerificationCode}   Competência: {d.Nfse.CompetenceDate:dd/MM/yyyy}");
        writer.Box("AUTORIZAÇÃO", official);

        writer.Space(4);
        if (d.Type == FiscalDocumentType.Nfe)
        {
            writer.Table(
                [("Código", 55), ("Descrição", 170), ("NCM", 50), ("CFOP", 32), ("Qtd", 40), ("Un", 25), ("Unit.", 55), ("Desc.", 45), ("Total", 51)],
                d.Items.OrderBy(i => i.LineNumber).Select(i => new[]
                {
                    i.Code, i.Description, i.Ncm ?? "", i.Cfop ?? "", i.Quantity.ToString("0.####", PtBr), i.Unit ?? "",
                    Money(i.UnitPrice), Money(i.Discount), Money(i.Total),
                }));
        }
        else
        {
            writer.Table(
                [("Cód. nacional", 70), ("Discriminação do serviço", 303), ("Desc.", 60), ("Valor", 90)],
                d.Items.Select(i => new[] { i.NationalTaxCode ?? "", i.Description, Money(i.Discount), Money(i.Total) }));
        }

        writer.Space(6);
        var totals = new List<string> { $"Valor dos itens: {Money(d.TotalItems)}", $"Descontos: {Money(d.TotalDiscount)}" };
        if (d.Type == FiscalDocumentType.Nfe)
        {
            totals.Add($"Frete: {Money(d.Nfe.Freight)}");
            totals.Add($"ICMS destacado: {Money(d.TotalTaxes)}");
        }
        else
        {
            totals.Add($"ISS: {Money(d.TotalTaxes)}{(d.Nfse.IssWithheld ? " (retido pelo tomador)" : "")}");
        }
        totals.Add($"VALOR TOTAL DA NOTA: {Money(d.TotalAmount)}");
        if (d.Type == FiscalDocumentType.Nfse && d.Nfse.IssWithheld) totals.Add($"Valor líquido: {Money(d.NetAmount)}");
        writer.Box("TOTAIS", totals);

        if (!string.IsNullOrWhiteSpace(d.AdditionalInfo))
            writer.Box("INFORMAÇÕES COMPLEMENTARES", [d.AdditionalInfo]);

        writer.Finish();
        using var ms = new MemoryStream();
        pdf.Save(ms);
        return ms.ToArray();
    }

    private static string TypeLabel(FiscalDocument d) => d.Type == FiscalDocumentType.Nfe ? "NF-e" : "NFS-e";
    private static string Money(decimal v) => v.ToString("C", PtBr);

    private static string Address(Address a) =>
        string.Join(" — ", new[]
        {
            $"{a.Street}, {a.Number} {a.Complement}".Trim().TrimEnd(','),
            a.District,
            $"{a.City}/{a.State}",
            string.IsNullOrEmpty(a.ZipCode) ? null : "CEP " + a.ZipCode,
        }.Where(p => !string.IsNullOrWhiteSpace(p) && p != "/"));

    private static string GroupKey(string? key) =>
        string.IsNullOrEmpty(key) || key.Any(ch => !char.IsDigit(ch)) ? key ?? "" : string.Join(" ", key.Chunk(4).Select(c => new string(c)));

    /// <summary>Escreve de cima para baixo, abrindo novas páginas quando necessário.</summary>
    private sealed class PageWriter
    {
        private readonly PdfDocument pdf;
        private readonly XFont regular = new("Arial", 8.5, XFontStyleEx.Regular);
        private readonly XFont bold = new("Arial", 8.5, XFontStyleEx.Bold);
        private readonly XFont small = new("Arial", 7, XFontStyleEx.Italic);
        private readonly XFont title = new("Arial", 14, XFontStyleEx.Bold);
        private XGraphics gfx = null!;
        private PdfPage page = null!;
        private double y;
        private double Width => page.Width.Point - 2 * Margin;
        private double Bottom => page.Height.Point - Margin;

        public PageWriter(PdfDocument pdf)
        {
            this.pdf = pdf;
            NewPage();
        }

        private void NewPage()
        {
            gfx?.Dispose();
            page = pdf.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            y = Margin;
        }

        private void Ensure(double height)
        {
            if (y + height > Bottom) NewPage();
        }

        public void Finish() => gfx.Dispose();

        public void Space(double h) => y += h;

        public void Banner(string text, XColor color)
        {
            Ensure(22);
            gfx.DrawRectangle(new XSolidBrush(color), Margin, y, Width, 18);
            gfx.DrawString(text, bold, XBrushes.White, new XRect(Margin + 6, y, Width - 12, 18), XStringFormats.CenterLeft);
            y += 22;
        }

        public void Title(string text)
        {
            Ensure(22);
            gfx.DrawString(text, title, XBrushes.Black, new XRect(Margin, y, Width, 20), XStringFormats.TopLeft);
            y += 20;
        }

        public void Small(string text)
        {
            gfx.DrawString(text, small, XBrushes.DimGray, new XRect(Margin, y, Width, 10), XStringFormats.TopLeft);
            y += 11;
        }

        public void Box(string caption, IEnumerable<string> lines)
        {
            var wrapped = lines.SelectMany(l => Wrap(l, regular, Width - 12)).ToList();
            var height = 14 + wrapped.Count * 11 + 4;
            Ensure(height + 4);
            gfx.DrawRectangle(XPens.Gray, Margin, y, Width, height);
            gfx.DrawString(caption, bold, XBrushes.Black, new XRect(Margin + 6, y + 3, Width, 10), XStringFormats.TopLeft);
            var ly = y + 15;
            foreach (var line in wrapped)
            {
                gfx.DrawString(line, regular, XBrushes.Black, new XRect(Margin + 6, ly, Width - 12, 10), XStringFormats.TopLeft);
                ly += 11;
            }
            y += height + 4;
        }

        public void Table((string Header, double Width)[] columns, IEnumerable<string[]> rows)
        {
            void Header()
            {
                Ensure(30);
                var x = Margin;
                gfx.DrawRectangle(XBrushes.LightGray, Margin, y, Width, 14);
                foreach (var (h, w) in columns)
                {
                    gfx.DrawString(h, bold, XBrushes.Black, new XRect(x + 2, y + 2, w - 4, 10), XStringFormats.TopLeft);
                    x += w;
                }
                y += 14;
            }

            Header();
            foreach (var row in rows)
            {
                var cells = row.Select((text, i) => Wrap(text, regular, columns[i].Width - 4)).ToList();
                var height = cells.Max(c => c.Count) * 10 + 4;
                if (y + height > Bottom) { NewPage(); Header(); }
                var x = Margin;
                for (var i = 0; i < columns.Length; i++)
                {
                    var ly = y + 2;
                    foreach (var line in cells[i])
                    {
                        gfx.DrawString(line, regular, XBrushes.Black, new XRect(x + 2, ly, columns[i].Width - 4, 10), XStringFormats.TopLeft);
                        ly += 10;
                    }
                    x += columns[i].Width;
                }
                y += height;
                gfx.DrawLine(XPens.LightGray, Margin, y, Margin + Width, y);
            }
        }

        private List<string> Wrap(string text, XFont font, double maxWidth)
        {
            var result = new List<string>();
            foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var current = "";
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (gfx.MeasureString(candidate, font).Width <= maxWidth) { current = candidate; continue; }
                    if (current.Length > 0) result.Add(current);
                    current = word;
                    // palavra maior que a coluna: quebra por caracteres
                    while (current.Length > 1 && gfx.MeasureString(current, font).Width > maxWidth)
                    {
                        var cut = current.Length - 1;
                        while (cut > 1 && gfx.MeasureString(current[..cut], font).Width > maxWidth) cut--;
                        result.Add(current[..cut]);
                        current = current[cut..];
                    }
                }
                result.Add(current);
            }
            return result;
        }
    }
}
