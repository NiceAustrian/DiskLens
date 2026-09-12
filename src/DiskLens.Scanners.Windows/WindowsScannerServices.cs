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
        if (!OperatingSystem.IsWindows()) return services;
        services.Replace(ServiceDescriptor.Singleton<IElevationService, WindowsElevationService>());
        services.Replace(ServiceDescriptor.Singleton<IFileOperations, WindowsFileOperations>());
        services.Replace(ServiceDescriptor.Singleton<INativeContextMenu, ShellContextMenu>());
        services.AddScanner<Ntfs.MftScanner>();
        return services;
    }
}
