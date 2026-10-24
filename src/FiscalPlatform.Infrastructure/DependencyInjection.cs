using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, DataPaths paths)
    {
        services.AddSingleton(paths);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={paths.DatabasePath}"));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        var dp = services.AddDataProtection()
            .SetApplicationName("FiscalPlatform")
            .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

        services.AddScoped<ICertificateStore, CertificateStore>();
        services.AddSingleton<IFiscalFileStore, FileStore>();
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<BackupService>();
        services.AddSingleton<IDocumentPdfRenderer, DocumentPdfRenderer>();
        services.AddHttpClient<RegistryLookup>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
            // As APIs públicas recusam (HTTP 429) chamadas sem identificação.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EmissorFiscal/1.0");
        });
        services.AddSingleton<IFiscalProvider>(new SimulatedFiscalProvider(FiscalDocumentType.Nfe));
        services.AddSingleton<IFiscalProvider>(new SimulatedFiscalProvider(FiscalDocumentType.Nfse));
        return services;
    }
}
