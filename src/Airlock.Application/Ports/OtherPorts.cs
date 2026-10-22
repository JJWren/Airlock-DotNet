using Airlock.Application.Events;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Ports;

/// <summary>Renders a finished scan as the PDF Scan Report; implemented by U3.</summary>
public interface IScanReportRenderer
{
    ValueTask<byte[]> RenderPdfAsync(Scan scan, CancellationToken cancellationToken);
}

/// <summary>The Live Log for every subscriber; implemented by U3's broadcaster.</summary>
public interface ILiveLog
{
    IAsyncEnumerable<LiveLogEntry> SubscribeAsync(CancellationToken cancellationToken);
}

/// <summary>Questions about the Airlock host's file system, answered with the host's own rules; implemented by U3.</summary>
public interface IHostFileSystem
{
    bool IsAbsolute(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>The full path, with <c>.</c> and <c>..</c> segments resolved.</summary>
    string GetFullPath(string path);

    /// <summary>The path with every symbolic link on the way followed.</summary>
    string ResolveLinks(string path);

    /// <summary>Whether <paramref name="path"/> is <paramref name="root"/> or lies inside it, comparing whole segments.</summary>
    bool IsWithin(string path, string root);
}
