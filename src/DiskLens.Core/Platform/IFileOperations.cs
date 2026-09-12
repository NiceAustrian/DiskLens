namespace DiskLens.Core.Platform;

/// <summary>Platform file operations the UI needs beyond reading: open, reveal, delete.</summary>
public interface IFileOperations
{
    /// <summary>Whether <see cref="DeleteAsync"/> moves to a recycle bin / trash (true) or deletes permanently (false).</summary>
    bool SupportsRecycleBin { get; }

    /// <summary>Opens a directory, or reveals a file in its directory, in the platform file manager.</summary>
    void RevealInFileManager(string path, bool isDirectory);

    /// <summary>Deletes a file or directory tree, to the recycle bin where supported.</summary>
    Task DeleteAsync(string path, bool isDirectory, CancellationToken ct);
}

/// <summary>Portable implementation: xdg-open / open / explorer, permanent deletion.</summary>
public sealed class GenericFileOperations : IFileOperations
{
    public bool SupportsRecycleBin => false;

    public void RevealInFileManager(string path, bool isDirectory)
    {
        var target = isDirectory ? path : System.IO.Path.GetDirectoryName(path) ?? path;
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", isDirectory ? $"\"{path}\"" : $"/select,\"{path}\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            System.Diagnostics.Process.Start("open", isDirectory ? [path] : ["-R", path]);
        else
            System.Diagnostics.Process.Start("xdg-open", [target]);
    }

    public Task DeleteAsync(string path, bool isDirectory, CancellationToken ct) => Task.Run(() =>
    {
        if (isDirectory) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }, ct);
}
