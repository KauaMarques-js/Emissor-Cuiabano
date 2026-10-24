using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FiscalPlatform.Domain;

namespace FiscalPlatform.Infrastructure;

/// <summary>Dados públicos para preencher cadastros automaticamente. Todos os campos são sugestões editáveis.</summary>
public sealed record RegistryData
{
    public string? Name { get; init; }
    public string? TradeName { get; init; }
    public string? ZipCode { get; init; }
    public string? Street { get; init; }
    public string? Number { get; init; }
    public string? Complement { get; init; }
    public string? District { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? CityIbgeCode { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    /// <summary>Nome do valor de <see cref="Domain.TaxRegime"/> deduzido do Simples Nacional/MEI.</summary>
    public string? TaxRegime { get; init; }
    public string? Situation { get; init; }
}

/// <summary>Consulta CNPJ (BrasilAPI) e CEP (ViaCEP).</summary>
public sealed class RegistryLookup(HttpClient http)
{
    private static readonly TextInfo Text = CultureInfo.GetCultureInfo("pt-BR").TextInfo;

    public async Task<RegistryData?> FindCnpjAsync(string cnpj, CancellationToken ct)
    {
        var json = await GetAsync($"https://brasilapi.com.br/api/cnpj/v1/{DocumentNumbers.Digits(cnpj)}", ct);
        if (json is null || Str(json, "razao_social") is null) return null;

        var type = Str(json, "descricao_tipo_de_logradouro");
        var street = Str(json, "logradouro");
        if (type is not null && street is not null && !street.StartsWith(type, StringComparison.OrdinalIgnoreCase)) street = $"{type} {street}";
        var number = Str(json, "numero");

        return new RegistryData
        {
            Name = Str(json, "razao_social"),
            TradeName = Str(json, "nome_fantasia"),
            ZipCode = Str(json, "cep"),
            Street = Title(street),
            Number = number is "SN" or "S/N" ? "S/N" : number,
            Complement = Title(Str(json, "complemento")),
            District = Title(Str(json, "bairro")),
            City = Title(Str(json, "municipio")),
            State = Str(json, "uf"),
            CityIbgeCode = Str(json, "codigo_municipio_ibge"),
            Email = Str(json, "email")?.ToLowerInvariant(),
            Phone = Str(json, "ddd_telefone_1"),
            TaxRegime = Bool(json, "opcao_pelo_mei") == true ? nameof(Domain.TaxRegime.Mei)
                : Bool(json, "opcao_pelo_simples") switch
                {
                    true => nameof(Domain.TaxRegime.SimplesNacional),
                    false => nameof(Domain.TaxRegime.RegimeNormal),
                    null => null,
                },
            Situation = Str(json, "descricao_situacao_cadastral"),
        };
    }

    public async Task<RegistryData?> FindCepAsync(string cep, CancellationToken ct)
    {
        var json = await GetAsync($"https://viacep.com.br/ws/{DocumentNumbers.Digits(cep)}/json/", ct);
        if (json is null || json["erro"] is not null) return null;
        return new RegistryData
        {
            ZipCode = DocumentNumbers.Digits(Str(json, "cep")),
            Street = Str(json, "logradouro"),
            District = Str(json, "bairro"),
            City = Str(json, "localidade"),
            State = Str(json, "uf"),
            CityIbgeCode = Str(json, "ibge"),
        };
    }

    private async Task<JsonObject?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonObject>(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonObject json, string key) =>
        json[key] is JsonValue v && v.ToString() is { Length: > 0 } s ? s.Trim() : null;

    private static bool? Bool(JsonObject json, string key) =>
        json[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static string? Title(string? value) => value is null ? null : Text.ToTitleCase(value.ToLowerInvariant());
}
