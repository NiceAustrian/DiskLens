using System.Diagnostics.CodeAnalysis;
using DiskLens.Core.Platform;
using DiskLens.Core.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DiskLens.Core;

public static class DiskLensCoreServices
{
    /// <summary>Registers the scanner-agnostic core. Platform modules add their scanners and providers on top.</summary>
    public static IServiceCollection AddDiskLensCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IScannerRegistry, ScannerRegistry>();
        services.TryAddSingleton<IScanService, ScanService>();
        services.TryAddSingleton<IVolumeService, VolumeService>();
        services.TryAddSingleton<IElevationService, NoElevationService>();
        services.TryAddSingleton<IFileOperations, GenericFileOperations>();
        services.TryAddSingleton<INativeContextMenu, NoNativeContextMenu>();
        return services;
    }

    public static IServiceCollection AddScanner<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TScanner>(this IServiceCollection services) where TScanner : class, IFileSystemScanner
    {
        services.AddSingleton<IFileSystemScanner, TScanner>();
        return services;
    }

    public static IServiceCollection AddVolumeProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(this IServiceCollection services) where TProvider : class, IVolumeProvider
    {
        services.AddSingleton<IVolumeProvider, TProvider>();
        return services;
    }
}
