using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Staging;

/// <summary>Creates Airlock's own folders under the staging root and deletes them again (IR-11, IR-13).</summary>
internal static class StagingFolders
{
    /// <summary>The marker file in every folder Airlock creates under the root; the startup sweep deletes only these.</summary>
    public const string MarkerName = ".airlock";

    /// <summary>The file a running Airlock holds open, so that one staging root serves one Airlock (IR-13).</summary>
    public const string LockName = ".airlock-lock";

    public const int DeleteAttempts = 5;

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(100);

    // IR-11: readable by the Airlock user only where the system has such permissions; Windows keeps the temp folder's.
    public static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerOnly);
        }
    }

    public static void CreateMarked(string path)
    {
        CreateDirectory(path);
        using var marker = OpenNew(Path.Combine(path, MarkerName));
    }

    public static bool IsMarked(string path) => File.Exists(Path.Combine(path, MarkerName));

    // IR-13, U4-NFR-09: held exclusively until disposed; .NET takes it as an advisory flock on Linux and macOS, and the
    // operating system releases it if the process dies.
    public static FileStream LockRoot(string root)
    {
        CreateDirectory(root);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerReadWrite;
        }

        try
        {
            return new FileStream(Path.Combine(root, LockName), options);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Another Airlock is using the staging root {root}", exception);
        }
    }

    /// <summary>A new file, never an existing one, owner-only on Linux and macOS.</summary>
    public static FileStream OpenNew(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = 0,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerReadWrite;
        }

        return new FileStream(path, options);
    }

    // IR-13: read-only entries are made writable and the deletion tried again at once; a file still locked — Syft being
    // killed on Windows — is waited for, 100, 200, 400 and 800 ms, before the failure goes to the caller.
    public static async Task DeleteAsync(string path, TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < DeleteAttempts)
            {
                if (MakeWritable(path))
                {
                    continue;
                }

                logger.DeletionRetried(attempt + 1, DeleteAttempts);
                await Task.Delay(delay, timeProvider, cancellationToken);
                delay *= 2;
            }
        }
    }

    // Archives Syft unpacks can carry read-only files and folders. Links are skipped, so nothing outside the folder changes.
    // Returns whether anything was changed.
    private static bool MakeWritable(string path)
    {
        var changed = false;
        try
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists)
            {
                return false;
            }

            changed = MakeWritable(root);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var entry in root.EnumerateFileSystemInfos("*", options))
            {
                changed |= MakeWritable(entry);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next attempt reports what still fails.
        }

        return changed;
    }

    private static bool MakeWritable(FileSystemInfo entry)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return false;
            }

            entry.Attributes &= ~FileAttributes.ReadOnly;
            return true;
        }

        if (entry is not DirectoryInfo || (entry.UnixFileMode & OwnerOnly) == OwnerOnly)
        {
            return false;
        }

        entry.UnixFileMode |= OwnerOnly;
        return true;
    }
}
