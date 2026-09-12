using DiskLens.Core;
using DiskLens.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DiskLens.Scanners.Windows;

public static class WindowsScannerServices
{
    /// <summary>Windows-native pieces: volume enumeration, elevation and (later) the NTFS MFT scanner.</summary>
    public static IServiceCollection AddWindowsScanners(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IElevationService, WindowsElevationService>());
        return services;
    }
}
