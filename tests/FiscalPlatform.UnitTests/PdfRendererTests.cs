using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;

namespace FiscalPlatform.UnitTests;

public class PdfRendererTests
{
    [Theory]
    [InlineData(FiscalDocumentType.Nfe)]
    [InlineData(FiscalDocumentType.Nfse)]
    public void Renders_pdf_for_authorized_document(FiscalDocumentType type)
    {
        var company = TestData.Company();
        var doc = type == FiscalDocumentType.Nfe ? TestData.NfeDraft(company.Id, 1) : TestData.NfseDraft(company.Id, 1);
        doc.Customer = TestData.Customer(company.Id);
        doc.Status = DocumentStatus.Autorizada;
        doc.Provider = ProviderKind.Simulacao;
        doc.Series = 1;
        doc.Number = 42;
        doc.AccessKey = "51261011222333000181550010000000421123456789";
        doc.AdditionalInfo = "Linha longa " + new string('x', 400);
        // muitos itens para forçar quebra de página
        for (var i = 0; i < 60 && type == FiscalDocumentType.Nfe; i++)
            doc.Items.Add(new FiscalDocumentItem { LineNumber = i + 2, Code = "P" + i, Description = "Produto com descrição comprida número " + i, Quantity = 1, UnitPrice = 10 });
        doc.Recalculate();

        var bytes = new DocumentPdfRenderer().Render(doc, company);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
