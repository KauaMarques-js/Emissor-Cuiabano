using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.Web.Controllers;

/// <summary>Preenchimento automático por CNPJ/CEP. Liberado sem login apenas durante o primeiro acesso.</summary>
[AllowAnonymous]
public class LookupController(RegistryLookup lookup, AppDbContext db) : Controller
{
    public async Task<IActionResult> Cnpj(string? value, CancellationToken ct)
    {
        if (!await AllowedAsync()) return Unauthorized();
        if (!DocumentNumbers.IsValidCnpj(value)) return BadRequest(new { message = "CNPJ inválido. Confira os números." });
        var data = await lookup.FindCnpjAsync(value!, ct);
        return data is null ? NotFound(new { message = "Não encontramos este CNPJ agora. Preencha os dados manualmente." }) : Json(data);
    }

    public async Task<IActionResult> Cep(string? value, CancellationToken ct)
    {
        if (!await AllowedAsync()) return Unauthorized();
        if (!DocumentNumbers.IsDigits(DocumentNumbers.Digits(value), 8)) return BadRequest(new { message = "CEP deve ter 8 números." });
        var data = await lookup.FindCepAsync(value!, ct);
        return data is null ? NotFound(new { message = "CEP não encontrado. Preencha o endereço manualmente." }) : Json(data);
    }

    private async Task<bool> AllowedAsync() => User.Identity?.IsAuthenticated == true || !await Setup.IsCompleteAsync(db);
}
