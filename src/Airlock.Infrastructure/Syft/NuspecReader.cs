using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml;
using Airlock.Application.Logging;
using Airlock.Domain.Targets;

namespace Airlock.Infrastructure.Syft;

/// <summary>The NuGet package a <c>.nupkg</c> names in its <c>.nuspec</c> (IR-04, Q-S0-3).</summary>
internal static class NuspecReader
{
    /// <summary>The most entries an archive may announce (U3-NFR-05); ZIP64 archives announce more.</summary>
    public const int MaxEntries = ushort.MaxValue - 1;

    /// <summary>How much of the <c>.nuspec</c> is read at most (U3-NFR-05).</summary>
    public const int MaxNuspecCharacters = 1024 * 1024;

    private const string NuGetType = "nuget";
    private const int EndRecordSize = 22;
    private const int EntryCountOffset = 10;
    private const int Zip64LocatorSize = 20;

    // IR-04: the package the .nuspec at the root names, type "nuget"; anything unreadable gives the reason instead.
    public static NuspecRead Read(Stream nupkg)
    {
        ArgumentNullException.ThrowIfNull(nupkg);
        try
        {
            if (EntryCountProblem(nupkg) is { } problem)
            {
                return new NuspecRead(null, problem);
            }

            nupkg.Position = 0;
            using var archive = new ZipArchive(nupkg, ZipArchiveMode.Read, leaveOpen: true);
            var nuspec = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.IndexOfAny(['/', '\\']) < 0 && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
            {
                return new NuspecRead(null, "the package has no .nuspec at its root");
            }

            if (nuspec.Length > MaxNuspecCharacters)
            {
                return new NuspecRead(null, "its .nuspec is larger than 1 MB");
            }

            using var stream = nuspec.Open();
            var (id, version) = ReadIdentity(stream);
            return id is null || version is null
                ? new NuspecRead(null, "its .nuspec names no id and version")
                : new NuspecRead(new Package(NuGetType, id, version), null);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or ArgumentException)
        {
            return new NuspecRead(null, ExternalText.Clean(exception.Message));
        }
    }

    // U3-NFR-05: before the archive's directory is loaded, its end record must announce at most 65,534 entries, without
    // ZIP64, so a crafted archive cannot make Airlock hold millions of entries.
    private static string? EntryCountProblem(Stream stream)
    {
        var length = stream.Length;
        if (length < EndRecordSize)
        {
            return "it is not a ZIP archive";
        }

        var tailLength = (int)Math.Min(length, EndRecordSize + ushort.MaxValue);
        var tail = new byte[tailLength];
        stream.Position = length - tailLength;
        stream.ReadExactly(tail);
        for (var i = tailLength - EndRecordSize; i >= 0; i--)
        {
            if (!IsSignature(tail, i, 0x05, 0x06))
            {
                continue;
            }

            if (BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + EntryCountOffset)) > MaxEntries
                || (i >= Zip64LocatorSize && IsSignature(tail, i - Zip64LocatorSize, 0x06, 0x07)))
            {
                return "it announces more than 65,534 entries";
            }

            return null;
        }

        return "it is not a ZIP archive";
    }

    private static bool IsSignature(byte[] bytes, int at, byte third, byte fourth) =>
        bytes[at] == 0x50 && bytes[at + 1] == 0x4B && bytes[at + 2] == third && bytes[at + 3] == fourth;

    // U3-NFR-07: no DTDs, no resolver, and no more than 1 MB of characters.
    private static (string? Id, string? Version) ReadIdentity(Stream nuspec)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxNuspecCharacters,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        using var reader = XmlReader.Create(nuspec, settings);
        string? id = null;
        string? version = null;
        string? section = null;
        reader.MoveToContent();
        while (!reader.EOF && (id is null || version is null))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (reader.Depth == 1)
            {
                section = reader.LocalName;
                reader.Read();
            }
            else if (reader.Depth == 2 && section == "metadata" && reader.LocalName is "id" or "version")
            {
                // <package><metadata><id>…</id><version>…</version>, whatever the namespace.
                var name = reader.LocalName;
                var value = reader.ReadElementContentAsString().Trim();
                if (name == "id")
                {
                    id = value;
                }
                else
                {
                    version = value;
                }
            }
            else
            {
                reader.Read();
            }
        }

        return (string.IsNullOrEmpty(id) ? null : id, string.IsNullOrEmpty(version) ? null : version);
    }
}

/// <summary>The NuGet package, or the reason the <c>.nuspec</c> could not be read.</summary>
internal sealed record NuspecRead(Package? Package, string? Problem);
