using System.Text.Json;
using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-05, U6-NFR-17 — every package Airlock.Web restores, transitive ones included, is in THIRD-PARTY-NOTICES.md at its
// version, nothing is listed that is not restored, and the licence texts it points to are there.
public sealed partial class NoticesTests
{
    private const string Assets = "src/Airlock.Web/obj/project.assets.json";

    [Fact]
    public void Every_restored_package_is_listed_at_its_version_and_only_those()
    {
        Assert.True(File.Exists(Repository.PathOf(Assets)), $"{Assets} is written by restoring these tests; run dotnet restore");
        using var assets = JsonDocument.Parse(Repository.Read(Assets));
        var restored = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "package")
            .Select(library => library.Name)
            .Order(StringComparer.OrdinalIgnoreCase);
        var listed = Row().Matches(Repository.Read("THIRD-PARTY-NOTICES.md"))
            .Select(row => $"{row.Groups["id"].Value}/{row.Groups["version"].Value}")
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(restored, listed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_licence_texts_it_points_to_are_in_the_repository_and_go_into_the_image()
    {
        var notices = Repository.Read("THIRD-PARTY-NOTICES.md");
        var files = LicenceFile().Matches(notices).Select(match => match.Groups[1].Value).Distinct().ToList();
        files.AddRange(TableRow().Matches(notices)
            .SelectMany(row => QuestPdfFile().Matches(row.Value))
            .Select(match => "licenses/questpdf/" + match.Groups[1].Value));

        Assert.Contains("licenses/syft/LICENSE", files);
        Assert.Contains("licenses/questpdf/LICENSE.md", files);
        Assert.Contains("licenses/microsoft-aspnetcore/THIRD-PARTY-NOTICES.TXT", files);
        Assert.All(files, file => Assert.True(File.Exists(Repository.PathOf(file)) || Directory.Exists(Repository.PathOf(file)), file));
        Assert.Contains("!licenses/", Repository.Lines(".dockerignore"));
    }

    [Theory]
    [InlineData("src/Airlock.Web/wwwroot/fonts/inter/OFL.txt", "Copyright (c) 2016 The Inter Project Authors")]
    [InlineData("src/Airlock.Web/wwwroot/fonts/jetbrains-mono/OFL.txt", "Copyright 2020 The JetBrains Mono Project Authors")]
    public void The_fonts_are_listed_with_the_copyright_their_licence_states(string licence, string copyright)
    {
        Assert.StartsWith(copyright, Repository.Read(licence), StringComparison.Ordinal);
        Assert.Contains(copyright, Repository.Read("THIRD-PARTY-NOTICES.md"), StringComparison.Ordinal);
    }

    // A package's row: | Id | Version | Licence | Copyright |.
    [GeneratedRegex(@"^\| (?<id>[A-Za-z][\w.]+) \| (?<version>\d+\.\d+\.\d+[\w.-]*) \|", RegexOptions.Multiline)]
    private static partial Regex Row();

    [GeneratedRegex(@"`(licenses/[\w./-]+)`")]
    private static partial Regex LicenceFile();

    [GeneratedRegex(@"^\|.*\|$", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    // In a table row, a file named alone is one of QuestPDF's native libraries' licence texts.
    [GeneratedRegex(@"`([\w-]+\.txt)`")]
    private static partial Regex QuestPdfFile();
}
