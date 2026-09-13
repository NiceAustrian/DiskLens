using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using DiskLens.Core;
using DiskLens.Core.Platform;
using DiskLens.Presentation.Shell;
using DiskLens.Scanners.Droid;
using DiskLens.Scanners.Generic;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiskLens.Droid;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Theme = "@style/DiskLensTheme",
    LaunchMode = LaunchMode.SingleTask,
    // Keep the activity (and the scan in it) alive across rotation and resizes; the view relayouts.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
                         | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : Activity
{
    private ServiceProvider? _services;
    private DiskLensView? _view;
    private AppShell? _shell;
    private bool _hadAllFilesAccess;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CrashReporter.Install(this);

        var services = new ServiceCollection();
        services.AddLogging(b => { b.AddProvider(new LogcatLoggerProvider()); b.SetMinimumLevel(LogLevel.Information); });
        services.AddDiskLensCore();
        services.AddScanner<WalkScanner>();            // portable walk; no DriveInfo provider on Android
        services.AddAndroidPlatform(this);   // activity context: settings pages opened from here return to us
        services.AddSingleton(_ => new UiRoot(IsSystemDark() ? DiskLens.UI.Rendering.Theme.Dark : DiskLens.UI.Rendering.Theme.Light));
        services.AddSingleton<AppShell>();
        _services = services.BuildServiceProvider();

        var root = _services.GetRequiredService<UiRoot>();
        _view = new DiskLensView(this, root);
        SetContentView(_view);

        _shell = _services.GetRequiredService<AppShell>();
        _shell.Attach(_view);
        ApplySystemBarStyle(root.Theme.IsDark);
        _view.DarkModeChanged += ApplySystemBarStyle;
        _hadAllFilesAccess = _services.GetRequiredService<IElevationService>().IsElevated;

        if (CrashReporter.TakeLastReport() is { } report)
            _view.Post(() => _shell.ShowNotice("DiskLens crashed last time", report, "Copy report"));
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view?.OnResume();
        // Coming back from the "All files access" settings page: refresh the home screen so the
        // banner disappears and scans see everything.
        if (_services is not null && _shell is not null)
        {
            var now = _services.GetRequiredService<IElevationService>().IsElevated;
            if (now && !_hadAllFilesAccess)
            {
                _hadAllFilesAccess = true;
                _view?.Post(_shell.ShowHome);
            }
        }
    }

    protected override void OnPause()
    {
        _view?.OnPause();
        base.OnPause();
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, Android.Views.KeyEvent? e)
    {
        if (keyCode == Keycode.Back && _view is not null && _shell is not null && _shell.CanGoBack)
        {
            // Escape closes popups and goes back to the drive picker; otherwise Android leaves the app.
            _view.DispatchKeySync(Key.Escape);
            return true;
        }
        return base.OnKeyDown(keyCode, e);
    }

    protected override void OnDestroy()
    {
        _services?.Dispose();
        base.OnDestroy();
    }

    /// <summary>Status/navigation bar icons must contrast with our title bar colour.</summary>
    private void ApplySystemBarStyle(bool dark)
    {
        if (Window?.InsetsController is { } controller)
        {
            var light = dark ? 0 : (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
            controller.SetSystemBarsAppearance(light, (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars));
        }
    }

    private bool IsSystemDark() =>
        (Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
}
