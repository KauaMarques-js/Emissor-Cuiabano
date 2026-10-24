using System.Globalization;
using FiscalPlatform.Application;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Integrations.Nfse;
using FiscalPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Dados (banco, chaves, certificados, XMLs, backups) ficam fora da pasta do projeto e da pasta pública.
var dataDir = builder.Configuration["App:DataDirectory"];
if (string.IsNullOrWhiteSpace(dataDir))
    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmissorFiscal");
var paths = new DataPaths(dataDir);
var fiscalOptions = builder.Configuration.GetSection("Fiscal").Get<FiscalOptions>() ?? new FiscalOptions();

builder.Services
    .AddInfrastructure(paths)
    .AddApplication(fiscalOptions)
    .AddNfseIntegration(builder.Configuration.GetSection("NfseNacional").Get<NfseNacionalOptions>());

builder.Services.AddSingleton<BatchQueue>();
builder.Services.AddSingleton<IBatchQueue>(sp => sp.GetRequiredService<BatchQueue>());
builder.Services.AddHostedService<BatchWorker>();
builder.Services.AddScoped<ActiveCompanyFilter>();
builder.Services.AddScoped<CompanyEditor>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var culture = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
app.Logger.LogInformation("Diretório de dados: {DataDir}", paths.Root);

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture],
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<SetupRedirectMiddleware>();
app.UseAuthorization();

// A página inicial é a emissão de nota; as demais seguem o padrão controller/ação (Index por omissão).
app.MapControllerRoute(name: "home", pattern: "", defaults: new { controller = "Documents", action = "New" });
app.MapControllerRoute(name: "default", pattern: "{controller}/{action=Index}/{id?}");

app.Run();
