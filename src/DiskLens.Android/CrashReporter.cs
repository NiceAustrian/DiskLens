using Android.Content;
using Android.Runtime;

namespace DiskLens.Droid;

/// <summary>
/// Writes unhandled exceptions to a file the user can reach (Download/DiskLens-crash.txt) so a
/// crash on a phone without adb still produces a stack trace. The shell shows the last report on
/// the next start.
/// </summary>
public static class CrashReporter
{
    private static string? _path;
    private static bool _installed;

    public static string? ReportPath => _path;

    public static void Install(Context context)
    {
        if (_installed) return;
        _installed = true;
        var downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
        var fallback = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath ?? "/sdcard";
        _path = Path.Combine(downloads ?? fallback, "DiskLens-crash.txt");

        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write("Android unhandled", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Write("Unobserved task", e.Exception); e.SetObserved(); };
    }

    /// <summary>Records an exception that was caught but should not have happened.</summary>
    public static void Write(string context, Exception? ex)
    {
        try
        {
            Android.Util.Log.Error("DiskLens", $"{context}: {ex}");
            if (_path is null) return;
            var text = $"DiskLens crash report\n{DateTime.Now:O}\n{context}\nDevice: {Android.OS.Build.Manufacturer} {Android.OS.Build.Model}, Android {Android.OS.Build.VERSION.Release} (API {(int)Android.OS.Build.VERSION.SdkInt})\nGPU/renderer: {Renderer}\n\n{ex}\n";
            File.WriteAllText(_path, text);
        }
        catch (Exception)
        {
            // Nothing sensible left to do.
        }
    }

    /// <summary>Filled in by the GL view once the context exists.</summary>
    public static string Renderer { get; set; } = "unknown";

    /// <summary>Returns and clears the last report, if any.</summary>
    public static string? TakeLastReport()
    {
        try
        {
            if (_path is null || !File.Exists(_path)) return null;
            var text = File.ReadAllText(_path);
            File.Delete(_path);
            return text;
        }
        catch (Exception) { return null; }
    }
}
