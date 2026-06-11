using EFiling.Core.Interfaces;
using EFiling.Core.Models;

namespace EFiling.Core.Factories;

/// <summary>
/// Default provider factory that resolves providers by name from a registered dictionary.
/// </summary>
public class DefaultProviderFactory : IProviderFactory
{
    private readonly Dictionary<string, IEFilingProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a provider instance.
    /// </summary>
    public void RegisterProvider(IEFilingProvider provider)
    {
        _providers[provider.ProviderName] = provider;
    }

    public IEFilingProvider GetProvider(CourtConfiguration config)
    {
        return GetProvider(config.ProviderType);
    }

    public IEFilingProvider GetProvider(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var provider))
            return provider;

        throw new InvalidOperationException($"No eFiling provider registered for '{providerName}'. " +
            $"Available providers: {string.Join(", ", _providers.Keys)}");
    }
}
