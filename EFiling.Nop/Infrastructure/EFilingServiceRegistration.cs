using EFiling.Core.Caching;
using EFiling.Core.Interfaces;
using EFiling.Nop.Caching;
using EFiling.Nop.Services;
using EFiling.Providers.JTI;
using Microsoft.Extensions.DependencyInjection;

namespace EFiling.Nop.Infrastructure;

/// <summary>
/// Registers all EFiling services into the nopCommerce DI container.
/// Call this from your nopCommerce plugin's ConfigureServices or Startup.
/// </summary>
public static class EFilingServiceRegistration
{
    /// <summary>
    /// Register all EFiling services. Call from nopCommerce plugin startup.
    /// </summary>
    /// <param name="services">The nopCommerce service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEFilingServices(this IServiceCollection services)
    {
        // Cache: NopEFilingCache wraps the nopCommerce IStaticCacheManager via INopCacheAdapter
        services.AddSingleton<IEFilingCache, NopEFilingCache>();

        // Provider: JTI provider (singleton — stateless, uses HttpClient internally)
        services.AddSingleton<IEFilingProvider>(sp =>
        {
            var cache = sp.GetRequiredService<IEFilingCache>();
            return new JtiEFilingProvider(cache);
        });

        // Court configuration service (SQL Server via IRepository<CourtConfigurationRecord>)
        services.AddScoped<DbCourtConfigurationService>();
        services.AddScoped<ICourtConfigurationService>(sp => sp.GetRequiredService<DbCourtConfigurationService>());

        // Draft service (SQL Server via IRepository<EFilingDraft>)
        services.AddScoped<IEFilingDraftService, EFilingDraftService>();

        // UD §1161.2 attestation audit service (SQL Server via IRepository<UdAccessAttestation>).
        // Step #43 — backs the UD-2 mandate from JTI EFM vendor
        // doc node/436#UnlawfulDetainer. See IUdAccessAttestationService for
        // verbatim source quote.
        services.AddScoped<IUdAccessAttestationService, UdAccessAttestationService>();

        // Order tracking service (SQL Server via IRepository<EFilingOrderRecord> + children)
        services.AddScoped<IEFilingOrderService, EFilingOrderService>();

        // Blob storage service for draft file uploads (reads Azure Blob plugin settings)
        services.AddScoped<IEFilingBlobService, EFilingBlobService>();

        // Filing status change notifications (email to customer)
        services.AddScoped<IEFilingNotificationService, EFilingNotificationService>();

        // Shared post-JTI-submit finalization (Braintree charge + EFilingOrderRecord creation).
        // Used by both CC AJAX (SubmitAndPayAjax) and SF form-post (CreateCase). P1.
        services.AddScoped<IFilingFinalizer, FilingFinalizer>();

        // Controller service layer
        services.AddScoped<Controllers.CourtFilingController>();

        // Scheduled tasks
        services.AddScoped<ScheduleTasks.NfrcPollingTask>();

        return services;
    }
}
