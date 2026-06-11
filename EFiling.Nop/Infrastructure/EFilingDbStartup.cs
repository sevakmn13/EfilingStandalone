using EFiling.Nop.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Migrations;

namespace EFiling.Nop.Infrastructure;

/// <summary>
/// Runs EFiling FluentMigrator migrations on application startup.
/// Must run after NopDbStartup (Order 10) so the DB and migration runner are available.
/// </summary>
public class EFilingDbStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        // Apply Installation migrations from the EFiling.Nop assembly
        using var scope = services.BuildServiceProvider().CreateScope();
        var migrationManager = scope.ServiceProvider.GetRequiredService<IMigrationManager>();
        var efilingAssembly = typeof(CourtConfigurationRecord).Assembly;

        migrationManager.ApplyUpMigrations(efilingAssembly, MigrationProcessType.Installation);
        migrationManager.ApplyUpMigrations(efilingAssembly, MigrationProcessType.Update);
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Run after NopDbStartup (10) so DB infrastructure is ready.
    /// </summary>
    public int Order => 11;
}
