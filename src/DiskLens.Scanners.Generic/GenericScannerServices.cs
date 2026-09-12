using DiskLens.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DiskLens.Scanners.Generic;

public static class GenericScannerServices
{
    /// <summary>The portable fallback scanner plus a DriveInfo-based volume provider. Register on every platform.</summary>
    public static IServiceCollection AddGenericScanners(this IServiceCollection services)
    {
        services.AddScanner<WalkScanner>();
        services.AddVolumeProvider<DriveInfoVolumeProvider>();
        return services;
    }
}
