using System.Runtime.CompilerServices;
using Android.Content;
using Android.OS;
using Android.OS.Storage;
using DiskLens.Core;
using DiskLens.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AEnvironment = Android.OS.Environment;

namespace DiskLens.Scanners.Droid;

public static class AndroidPlatformServices
{
    /// <summary>Android volumes, the "All files access" permission as elevation, and file operations without a file manager.</summary>
    public static IServiceCollection AddAndroidPlatform(this IServiceCollection services, Context context)
    {
        services.AddSingleton(context);
        services.AddVolumeProvider<AndroidVolumeProvider>();
        services.Replace(ServiceDescriptor.Singleton<IElevationService, AndroidStorageAccess>());
        services.Replace(ServiceDescriptor.Singleton<IFileOperations, AndroidFileOperations>());
        return services;
    }
}

/// <summary>
/// Internal shared storage, removable cards/USB drives (via StorageManager) and the app's own data
/// directory. Sizes come from StatFs. The generic DriveInfo provider is not registered on Android
/// – it reports every mount point including dozens of pseudo file systems.
/// </summary>
public sealed class AndroidVolumeProvider(Context context) : IVolumeProvider
{
    public async IAsyncEnumerable<VolumeInfo> EnumerateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (context.GetSystemService(Context.StorageService) is StorageManager sm)
        {
            foreach (var volume in sm.StorageVolumes)
            {
                var dir = volume.Directory?.AbsolutePath;
                if (dir is null || !seen.Add(dir)) continue;
                var ready = volume.State == AEnvironment.MediaMounted || volume.State == AEnvironment.MediaMountedReadOnly;
                var label = volume.GetDescription(context) ?? (volume.IsPrimary ? "Internal storage" : "External storage");
                var kind = volume.IsRemovable ? VolumeKind.Removable : VolumeKind.Fixed;
                var (total, free) = ready ? Stat(dir) : (0L, 0L);
                // Id doubles as the display name here – Android has no drive letters worth showing.
                yield return new VolumeInfo(label, dir, "", FileSystemOf(dir), kind, total, free, ready);
            }
        }

        // Fallback if StorageManager gave us nothing usable.
        var primary = AEnvironment.ExternalStorageDirectory?.AbsolutePath;
        if (primary is not null && seen.Add(primary))
        {
            var (total, free) = Stat(primary);
            yield return new VolumeInfo("Internal storage", primary, "", FileSystemOf(primary), VolumeKind.Fixed, total, free);
        }
    }

    private static (long Total, long Free) Stat(string path)
    {
        try
        {
            var st = new StatFs(path);
            return (st.TotalBytes, st.AvailableBytes);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static string FileSystemOf(string path)
    {
        // /proc/mounts lists "device mountpoint fstype options"; the longest matching mount point wins.
        try
        {
            string best = "", fs = "";
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ');
                if (parts.Length < 3) continue;
                var mount = parts[1];
                if (path.StartsWith(mount, StringComparison.Ordinal) && mount.Length > best.Length)
                {
                    best = mount; fs = parts[2];
                }
            }
            return fs is "fuse" or "sdcardfs" ? "fuse (emulated)" : fs;
        }
        catch (Exception)
        {
            return "";
        }
    }
}

/// <summary>
/// Android 11+ hides most of shared storage from apps unless they hold MANAGE_EXTERNAL_STORAGE
/// ("All files access"). That is the platform's equivalent of elevation for us.
/// </summary>
public sealed class AndroidStorageAccess(Context context) : IElevationService
{
    public bool IsElevated => Build.VERSION.SdkInt < BuildVersionCodes.R || AEnvironment.IsExternalStorageManager;

    public bool CanRelaunchElevated => !IsElevated;

    public ElevationPrompt? Prompt => IsElevated ? null : new(
        "Allow access to all files",
        "Android only shows media folders to apps by default. Grant \"All files access\" so DiskLens can measure everything on the device.",
        "Open settings");

    public bool RelaunchElevated(string[] args)
    {
        try
        {
            var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception)
        {
            try
            {
                var intent = new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

/// <summary>No file manager to reveal in; deletion is permanent (Android has no recycle bin for arbitrary files).</summary>
public sealed class AndroidFileOperations : IFileOperations
{
    public bool SupportsRecycleBin => false;
    public bool CanReveal => false;

    public void RevealInFileManager(string path, bool isDirectory) { }

    public Task DeleteAsync(string path, bool isDirectory, CancellationToken ct) => Task.Run(() =>
    {
        if (isDirectory) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }, ct);
}
