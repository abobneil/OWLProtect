using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class ConfigurationPlatformBootstrapSettingsProvider(IOptions<PlatformBootstrapOptions> options) : IPlatformBootstrapSettingsProvider
{
    private readonly PlatformBootstrapSettings _settings = new(
        options.Value.DefaultTenantId.Trim(),
        options.Value.DefaultTenantName.Trim(),
        options.Value.DefaultTenantRegion.Trim(),
        options.Value.SeedTestUserEnabled,
        options.Value.SessionRevalidationSeconds < 30 ? 30 : options.Value.SessionRevalidationSeconds);

    public PlatformBootstrapSettings GetSettings() => _settings;
}
