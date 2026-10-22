using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests.Targets;

// BR-12
public sealed class ManifestCatalogTests
{
    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("requirements-dev.txt")]
    [InlineData("REQUIREMENTS.TXT")]
    [InlineData("package-lock.json")]
    [InlineData("poetry.lock")]
    [InlineData("go.mod")]
    [InlineData("Cargo.lock")]
    [InlineData("pom.xml")]
    [InlineData("Gemfile.lock")]
    [InlineData("packages.lock.json")]
    [InlineData("app.deps.json")]
    [InlineData("airlock.gemspec")]
    public void Known_dependency_declarations_are_manifests(string fileName)
    {
        Assert.True(ManifestCatalog.IsManifest(fileName));
        Assert.Equal(ArtifactKind.Manifest, new Artifact(fileName, 56).Kind);
    }

    [Theory]
    [InlineData("app.jar")]
    [InlineData("alpine-3.20.tar")]
    [InlineData("bundle.zip")]
    [InlineData("PyYAML-5.3-cp38-cp38-win_amd64.whl")]
    [InlineData("requirements.json")]
    [InlineData("requirements")]
    [InlineData("notes.docx")]
    public void Everything_else_is_another_artifact(string fileName)
    {
        Assert.False(ManifestCatalog.IsManifest(fileName));
        Assert.Equal(ArtifactKind.Other, new Artifact(fileName, 1024).Kind);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData(@"..\evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void An_artifact_name_must_be_a_plain_file_name(string fileName)
    {
        Assert.Throws<ArgumentException>(() => new Artifact(fileName, 1));
    }

    [Fact]
    public void An_artifact_size_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Artifact("requirements.txt", -1));
        Assert.Equal(56, new Artifact("requirements.txt", 56).SizeBytes);
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ManifestCatalog.IsManifest(null!));
    }
}
