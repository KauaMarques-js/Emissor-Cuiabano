using System.Net;
using System.Text;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;

namespace FiscalPlatform.UnitTests;

public class RegistryLookupTests
{
    private sealed class FakeHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Cnpj_lookup_maps_company_data_and_regime()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {"razao_social":"PERFUMARIA TESTE LTDA","nome_fantasia":"","descricao_tipo_de_logradouro":"RUA",
             "logradouro":"DAS FLORES","numero":"SN","complemento":"","bairro":"CENTRO NORTE","municipio":"CUIABA","uf":"MT",
             "cep":"78005000","codigo_municipio_ibge":5103403,"opcao_pelo_simples":true,"opcao_pelo_mei":false,
             "descricao_situacao_cadastral":"ATIVA","email":null}
            """);
        var data = await new RegistryLookup(new HttpClient(handler)).FindCnpjAsync("11.222.333/0001-81", CancellationToken.None);

        Assert.EndsWith("/cnpj/v1/11222333000181", handler.Requested!.AbsolutePath);
        Assert.Equal("PERFUMARIA TESTE LTDA", data!.Name);
        Assert.Null(data.TradeName);
        Assert.Equal("Rua Das Flores", data.Street);
        Assert.Equal("S/N", data.Number);
        Assert.Equal("Cuiaba", data.City);
        Assert.Equal("5103403", data.CityIbgeCode);
        Assert.Equal(nameof(TaxRegime.SimplesNacional), data.TaxRegime);
    }

    [Theory]
    [InlineData("true", "false", nameof(TaxRegime.Mei))]
    [InlineData("false", "true", nameof(TaxRegime.SimplesNacional))]
    [InlineData("false", "false", nameof(TaxRegime.RegimeNormal))]
    [InlineData("false", "null", null)]
    public async Task Regime_comes_from_simples_and_mei_flags(string mei, string simples, string? expected)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, $$"""{"razao_social":"X","opcao_pelo_mei":{{mei}},"opcao_pelo_simples":{{simples}}}""");
        var data = await new RegistryLookup(new HttpClient(handler)).FindCnpjAsync("11222333000181", CancellationToken.None);
        Assert.Equal(expected, data!.TaxRegime);
    }

    [Fact]
    public async Task Cep_lookup_returns_address_or_null_when_not_found()
    {
        var found = await new RegistryLookup(new HttpClient(new FakeHandler(HttpStatusCode.OK,
            """{"cep":"78005-000","logradouro":"Rua Engenheiro Ricardo Franco","bairro":"Centro-Norte","localidade":"Cuiabá","uf":"MT","ibge":"5103403"}""")))
            .FindCepAsync("78005-000", CancellationToken.None);
        Assert.Equal("78005000", found!.ZipCode);
        Assert.Equal("5103403", found.CityIbgeCode);

        var missing = await new RegistryLookup(new HttpClient(new FakeHandler(HttpStatusCode.OK, """{"erro":"true"}""")))
            .FindCepAsync("99999999", CancellationToken.None);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Service_failure_returns_null()
    {
        var data = await new RegistryLookup(new HttpClient(new FakeHandler(HttpStatusCode.NotFound, "{}")))
            .FindCnpjAsync("11222333000181", CancellationToken.None);
        Assert.Null(data);
    }
}
