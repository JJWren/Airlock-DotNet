using Airlock.Domain.Scanning;

namespace Airlock.Application.Ports;

/// <summary>Holds the latest version of every retained scan, in memory; implemented by U3.</summary>
public interface IScanRegistry
{
    void Add(Scan scan);

    Scan? Find(ScanId id);

    /// <summary>Replaces the latest version of a scan with the transition's result, atomically, and returns it.</summary>
    Scan Update(ScanId id, Func<Scan, Scan> transition);

    IReadOnlyList<Scan> List();

    void Remove(ScanId id);
}
