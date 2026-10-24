using System.Buffers.Binary;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;

namespace Airlock.Infrastructure.Tests.Syft;

// IR-04, Q-S0-3 — the package a .nupkg names; U3-NFR-05 and U3-NFR-07 — hostile packages are read with caps.
public sealed class NuspecReaderTests
{
    [Fact]
    public void A_NuGet_package_names_its_identity_in_the_nuspec_at_its_root()
    {
        var read = Read(A.Zip(("Newtonsoft.Json.nuspec", A.Nuspec()), ("lib/net45/Newtonsoft.Json.dll", "not really an assembly")));

        Assert.Null(read.Problem);
        Assert.Equal("nuget", read.Package!.Type);
        Assert.Equal("Newtonsoft.Json 12.0.1", read.Package.ToString());
    }

    [Fact]
    public void A_nuspec_without_a_namespace_is_read_too()
    {
        var read = Read(A.Zip(("x.nuspec", "<package><metadata><version>2.0.0</version><id>Plain</id></metadata></package>")));

        Assert.Equal("Plain 2.0.0", read.Package!.ToString());
    }

    [Fact]
    public void Only_ids_inside_the_metadata_count()
    {
        var read = Read(A.Zip(("x.nuspec", "<package><files><id>Wrong</id></files><metadata><id>Right</id><version>1.0.0</version></metadata></package>")));

        Assert.Equal("Right 1.0.0", read.Package!.ToString());
    }

    [Theory]
    [InlineData("lib/Newtonsoft.Json.nuspec")]
    [InlineData("Newtonsoft.Json.txt")]
    public void A_package_without_a_nuspec_at_its_root_adds_nothing(string entry)
    {
        var read = Read(A.Zip((entry, A.Nuspec())));

        Assert.Null(read.Package);
        Assert.Equal("the package has no .nuspec at its root", read.Problem);
    }

    [Theory]
    [InlineData("<package><metadata><id>Broken</id>")]
    [InlineData("<package><metadata><version>1.0</version></metadata></package>")]
    [InlineData("<package><metadata><id>  </id><version>1.0</version></metadata></package>")]
    public void A_broken_nuspec_adds_nothing_and_says_why(string nuspec)
    {
        var read = Read(A.Zip(("x.nuspec", nuspec)));

        Assert.Null(read.Package);
        Assert.False(string.IsNullOrWhiteSpace(read.Problem));
    }

    [Fact]
    public void A_nuspec_with_a_DTD_is_refused()
    {
        var read = Read(A.Zip(("x.nuspec",
            "<?xml version=\"1.0\"?><!DOCTYPE package [<!ENTITY big \"x\">]><package><metadata><id>&big;</id><version>1</version></metadata></package>")));

        Assert.Null(read.Package);
        Assert.Contains("DTD", read.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_nuspec_larger_than_1_MB_is_refused()
    {
        var huge = "<package><metadata><id>Big</id><version>1.0</version><description>"
            + new string(' ', NuspecReader.MaxNuspecCharacters) + "</description></metadata></package>";

        var read = Read(A.Zip(("x.nuspec", huge)));

        Assert.Null(read.Package);
        Assert.Equal("its .nuspec is larger than 1 MB", read.Problem);
    }

    [Fact]
    public void An_archive_announcing_more_than_65534_entries_is_refused_before_its_directory_is_read()
    {
        var zip = A.Zip(("x.nuspec", A.Nuspec()));
        var record = LastIndexOf(zip, [0x50, 0x4B, 0x05, 0x06]);
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(record + 8), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(record + 10), ushort.MaxValue);

        var read = Read(zip);

        Assert.Null(read.Package);
        Assert.Equal("it announces more than 65,534 entries", read.Problem);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 })]
    public void Something_that_is_not_a_ZIP_archive_is_refused(byte[] bytes)
    {
        var read = Read(bytes);

        Assert.Null(read.Package);
        Assert.Equal("it is not a ZIP archive", read.Problem);
    }

    private static NuspecRead Read(byte[] nupkg)
    {
        using var stream = new MemoryStream(nupkg);
        return NuspecReader.Read(stream);
    }

    private static int LastIndexOf(byte[] bytes, byte[] pattern) => bytes.AsSpan().LastIndexOf(pattern);
}
