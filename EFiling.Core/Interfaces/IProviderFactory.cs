using EFiling.Core.Models;

namespace EFiling.Core.Interfaces;

/// <summary>
/// Factory that resolves the correct <see cref="IEFilingProvider"/> for a given court configuration.
/// </summary>
public interface IProviderFactory
{
    /// <summary>
    /// Get the provider for a court configuration based on its <see cref="CourtConfiguration.ProviderType"/>.
    /// </summary>
    IEFilingProvider GetProvider(CourtConfiguration config);

    /// <summary>
    /// Get a provider by its name directly (e.g., "JTI").
    /// </summary>
    IEFilingProvider GetProvider(string providerName);
}
