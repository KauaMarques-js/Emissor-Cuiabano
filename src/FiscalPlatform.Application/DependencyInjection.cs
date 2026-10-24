using Microsoft.Extensions.DependencyInjection;

namespace FiscalPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, FiscalOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IFiscalProviderResolver, FiscalProviderResolver>();
        services.AddScoped<DocumentService>();
        services.AddScoped<EmissionService>();
        services.AddScoped<BatchService>();
        services.AddSingleton<BatchProcessor>();
        return services;
    }
}
