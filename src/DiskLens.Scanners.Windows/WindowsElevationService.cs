using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using DiskLens.Core.Platform;

namespace DiskLens.Scanners.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool CanRelaunchElevated => !IsElevated && Environment.ProcessPath is not null;

    public bool RelaunchElevated(string[] args)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined.
            return false;
        }
    }
}
