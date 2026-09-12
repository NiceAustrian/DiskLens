using Microsoft.Extensions.DependencyInjection;

namespace DiskLens.Scanners.Posix;

public static class PosixScannerServices
{
    /// <summary>Linux/macOS specifics. Currently relies on the generic scanner; native mount enumeration follows.</summary>
    public static IServiceCollection AddPosixScanners(this IServiceCollection services)
    {
        return services;
    }
}
