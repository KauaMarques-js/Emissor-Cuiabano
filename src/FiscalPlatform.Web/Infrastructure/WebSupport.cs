using System.Globalization;
using System.Text;
using System.Threading.Channels;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using FiscalPlatform.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Web.Infrastructure;

/// <summary>O primeiro acesso só termina quando existe uma empresa (conta + empresa são criadas juntas).</summary>
public static class Setup
{
    private static volatile bool complete;

    public static async Task<bool> IsCompleteAsync(AppDbContext db) => complete || (complete = await db.Companies.AnyAsync());

    public static void MarkComplete() => complete = true;
}

/// <summary>Enquanto o primeiro acesso não termina, qualquer página leva à tela de criação da conta.</summary>
public class SetupRedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var path = ctx.Request.Path.Value ?? "";
        var allowed = Path.HasExtension(path)
                      || path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
                      || path.StartsWith("/Lookup", StringComparison.OrdinalIgnoreCase);
        if (!allowed && !await Setup.IsCompleteAsync(db))
        {
            ctx.Response.Redirect("/Account/Register");
            return;
        }
        await next(ctx);
    }
}

/// <summary>Empresa ativa guardada em cookie; sempre revalidada no servidor.</summary>
public static class ActiveCompany
{
    public const string CookieName = "EmpresaAtiva";
    public const string ItemKey = "ActiveCompany";

    public static int? ReadId(HttpContext ctx) =>
        int.TryParse(ctx.Request.Cookies[CookieName], out var id) ? id : null;

    public static void Set(HttpContext ctx, int id) =>
        ctx.Response.Cookies.Append(CookieName, id.ToString(), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Expires = DateTimeOffset.Now.AddYears(1),
        });
}

public class ActiveCompanyFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var id = ActiveCompany.ReadId(context.HttpContext);
        var company = id is null ? null : await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.IsActive);
        if (company is null)
        {
            if (context.Controller is Controller c)
                c.TempData["Error"] = "Selecione (ou cadastre) a empresa ativa antes de continuar.";
            context.Result = new RedirectToActionResult("Index", "Companies", null);
            return;
        }
        context.HttpContext.Items[ActiveCompany.ItemKey] = company;
        await next();
    }
}

public class RequireCompanyAttribute() : TypeFilterAttribute(typeof(ActiveCompanyFilter));

public abstract class AppController : Controller
{
    protected Company Company => (Company)HttpContext.Items[ActiveCompany.ItemKey]!;
    protected int CompanyId => Company.Id;
    protected string UserName => User.Identity?.Name ?? "desconhecido";

    protected void Success(string message) => TempData["Message"] = message;
    protected void Failure(string message) => TempData["Error"] = message;

    protected void AddErrors(IEnumerable<ValidationError> errors, string? prefix = null)
    {
        foreach (var e in errors) ModelState.AddModelError(prefix is null ? e.Field : $"{prefix}.{e.Field}", e.Message);
    }

    protected FileContentResult Csv(string fileName, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(string.Join(';', row.Select(v => "\"" + Convert.ToString(v, CultureInfo.GetCultureInfo("pt-BR"))?.Replace("\"", "\"\"") + "\"")));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}

public interface IPaged
{
    int Page { get; }
    int TotalPages { get; }
    int Total { get; }
}

public sealed class Paged<T>(List<T> items, int page, int pageSize, int total) : IPaged
{
    public List<T> Items { get; } = items;
    public int Page { get; } = page;
    public int Total { get; } = total;
    public int TotalPages { get; } = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
}

public static class PagingExtensions
{
    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize = 25)
    {
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new Paged<T>(items, page, pageSize, total);
    }
}

/// <summary>Formatação para as views.</summary>
public static class Ui
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Label(Enum value) => EnumText.Of(value);

    public static string Money(decimal value) => value.ToString("C", PtBr);
    public static string Doc(string? value) => DocumentNumbers.Format(value);
    public static string Date(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm") ?? "";

    public static string TypeLabel(FiscalDocumentType type) => type == FiscalDocumentType.Nfe ? "NF-e" : "NFS-e";

    public static string StatusClass(DocumentStatus s) => s switch
    {
        DocumentStatus.Autorizada => "text-bg-success",
        DocumentStatus.Rejeitada => "text-bg-danger",
        DocumentStatus.FalhaComunicacao or DocumentStatus.Pendente or DocumentStatus.Enviando => "text-bg-warning",
        DocumentStatus.Cancelada => "text-bg-dark",
        _ => "text-bg-secondary",
    };

    public static string BatchItemClass(BatchItemStatus s) => s switch
    {
        BatchItemStatus.Autorizada => "text-bg-success",
        BatchItemStatus.Rejeitada => "text-bg-danger",
        BatchItemStatus.NaoEnviado => "text-bg-secondary",
        BatchItemStatus.Aguardando or BatchItemStatus.Processando => "text-bg-light",
        _ => "text-bg-warning",
    };

    public static string CertificateClass(CertificateStatus s) => s switch
    {
        CertificateStatus.Valido => "text-bg-success",
        CertificateStatus.ProximoVencimento => "text-bg-warning",
        _ => "text-bg-danger",
    };
}

/// <summary>Fila em memória dos lotes; o estado persistido no banco permite retomar após reinício.</summary>
public sealed class BatchQueue : IBatchQueue
{
    private readonly Channel<int> channel = Channel.CreateUnbounded<int>();
    public void Enqueue(int batchId) => channel.Writer.TryWrite(batchId);
    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

public sealed class BatchWorker(BatchQueue queue, BatchProcessor processor, IServiceScopeFactory scopes, ILogger<BatchWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in await db.Batches.Where(b => b.Status == BatchStatus.Processando).Select(b => b.Id).ToListAsync(stoppingToken))
                queue.Enqueue(id);
        }

        await foreach (var batchId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await processor.ProcessAsync(batchId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao processar o lote {BatchId}", batchId);
            }
        }
    }
}
