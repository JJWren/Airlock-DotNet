using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace Airlock.Infrastructure.Tests.Syft;

// The real Syft (Category=Integration): run when the IR-06 search, or SYFT_BINARY_PATH, finds it; skipped otherwise.
[Trait("Category", "Integration")]
public sealed class SyftIntegrationTests : IDisposable
{
    private static readonly SyftLocation Syft = new SyftLocator(Environment.GetEnvironmentVariable("SYFT_BINARY_PATH")).Locate();

    private readonly TemporaryFolder folder = new();

    [Fact]
    public async Task A_requirements_file_gives_its_four_python_packages()
    {
        var target = folder.Combine("target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(
            Path.Combine(target, "requirements.txt"), "flask==0.12.2\njinja2==2.10\npyyaml==5.3\nrequests==2.25.1\n", Test.Token);

        var produced = await ProduceAsync(new DirectorySource(target));

        Assert.Equal(
            ["flask 0.12.2", "jinja2 2.10", "pyyaml 5.3", "requests 2.25.1"],
            produced.Packages.Select(package => package.Package.ToString()).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_wheel_staged_as_zip_gives_its_package()
    {
        var wheel = A.Zip(
            ("PyYAML-5.3.dist-info/METADATA", "Metadata-Version: 2.1\nName: PyYAML\nVersion: 5.3\n"),
            ("PyYAML-5.3.dist-info/WHEEL", "Wheel-Version: 1.0\nGenerator: bdist_wheel\nRoot-Is-Purelib: false\n"),
            ("PyYAML-5.3.dist-info/RECORD", string.Empty),
            ("yaml/__init__.py", "__version__ = '5.3'\n"));

        var produced = await ProduceAsync(await StageAsync("PyYAML-5.3-cp38-cp38-win_amd64.whl", wheel));

        Assert.Contains(produced.Packages, package => package.Package is { Type: "python", Version: "5.3" }
            && package.Package.Name.Equals("pyyaml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_NuGet_package_gives_the_package_its_nuspec_names()
    {
        var produced = await ProduceAsync(await StageAsync("Newtonsoft.Json.12.0.1.nupkg", A.Zip(("Newtonsoft.Json.nuspec", A.Nuspec()))));

        Assert.Contains(produced.Packages, package => package.Package.ToString() == "Newtonsoft.Json 12.0.1" && package.Package.Type == "nuget");
    }

    [Fact]
    public async Task An_image_archive_is_recognized_as_a_container_image()
    {
        var image = folder.Combine("alpine.tar");
        await File.WriteAllBytesAsync(image, ImageArchive(), Test.Token);

        var produced = await ProduceAsync(new FileSource(image, new Domain.Targets.Artifact("alpine.tar", new FileInfo(image).Length)));

        Assert.True(produced.IsContainerImage);
        Assert.Contains(produced.Packages, package => package.Package is { Type: "apk", Name: "busybox", Version: "1.36.1-r31" });
    }

    [Fact]
    public async Task Syfts_own_binary_gives_its_Go_modules_with_their_leading_v()
    {
        Assert.SkipUnless(Syft.Exists, "Syft is not installed; set SYFT_BINARY_PATH or run scripts/install-tools.");

        var produced = await ProduceAsync(new FileSource(Syft.Path, new Domain.Targets.Artifact(SyftLocator.FileName, new FileInfo(Syft.Path).Length)));

        Assert.True(produced.Packages.Count > 200);
        Assert.Contains(produced.Packages, package => package.Package.Type == "go-module" && package.Package.Version.StartsWith('v'));
    }

    [Fact]
    public async Task Cancelling_a_real_run_leaves_no_Syft_process_behind()
    {
        Assert.SkipUnless(Syft.Exists, "Syft is not installed; set SYFT_BINARY_PATH or run scripts/install-tools.");
        var processName = Path.GetFileNameWithoutExtension(Syft.Path);
        var before = Process.GetProcessesByName(processName).Length;
        var large = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Test.Token);
        var run = Generator().GenerateAsync(new DirectorySource(Directory.Exists(large) ? large : AppContext.BaseDirectory), cancel.Token).AsTask();
        await Task.Delay(1_500, Test.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5), Test.Token));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Process.GetProcessesByName(processName).Length > before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Test.Token);
        }

        Assert.True(Process.GetProcessesByName(processName).Length <= before);
    }

    public void Dispose() => folder.Dispose();

    /// <summary>A docker-archive image: one layer with Alpine's release file and its package database.</summary>
    private static byte[] ImageArchive()
    {
        var layer = Tar(
            ("etc/os-release", "NAME=\"Alpine Linux\"\nID=alpine\nVERSION_ID=3.20.3\nPRETTY_NAME=\"Alpine Linux v3.20\"\n"),
            ("lib/apk/db/installed", "C:Q1abc=\nP:busybox\nV:1.36.1-r31\nA:x86_64\nS:500000\nI:900000\nT:Size optimized toolbox\nU:https://busybox.net/\nL:GPL-2.0-only\no:busybox\nm:Sören Tempel <soeren+alpine@soeren-tempel.net>\nt:1720000000\nc:abc\n\n"));
        var diffId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(layer));
        var config = "{\"architecture\":\"amd64\",\"os\":\"linux\",\"config\":{},\"rootfs\":{\"type\":\"layers\",\"diff_ids\":[\"" + diffId + "\"]}}";
        var manifest = "[{\"Config\":\"config.json\",\"RepoTags\":[\"airlock/test:1\"],\"Layers\":[\"layer.tar\"]}]";
        return Tar(("manifest.json", manifest), ("config.json", config), ("layer.tar", layer));
    }

    private static byte[] Tar(params (string Name, object Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var bytes = content as byte[] ?? Encoding.UTF8.GetBytes((string)content);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(bytes) });
            }
        }

        return buffer.ToArray();
    }

    private async Task<SbomProduced> ProduceAsync(SbomSource source)
    {
        Assert.SkipUnless(Syft.Exists, "Syft is not installed; set SYFT_BINARY_PATH or run scripts/install-tools.");
        using var deadline = Test.Deadline(120);
        var outcome = await Generator().GenerateAsync(source, deadline.Token);
        return outcome as SbomProduced ?? throw new InvalidOperationException(((SbomFailed)outcome).Failure.Reason);
    }

    private async Task<FileSource> StageAsync(string name, byte[] content)
    {
        var staging = new DiskArtifactStaging(
            Microsoft.Extensions.Options.Options.Create(new StagingOptions { Root = folder.Combine("staging") }),
            TimeProvider.System,
            NullLogger<DiskArtifactStaging>.Instance);
        var area = await staging.CreateAsync(ScanId.New(), Test.Token);
        using var stream = new MemoryStream(content);
        var staged = Assert.IsType<ArtifactStaged>(await area.AddAsync(name, stream, long.MaxValue, Test.Token));
        return new FileSource(staged.Artifact.Path, staged.Artifact.Artifact);
    }

    private SyftSbomGenerator Generator() =>
        new(
            Microsoft.Extensions.Options.Options.Create(new SyftOptions { BinaryPath = Syft.Path }),
            Microsoft.Extensions.Options.Options.Create(new NvdOptions()),
            Microsoft.Extensions.Options.Options.Create(new StagingOptions { Root = folder.Combine("syft-staging") }),
            Microsoft.Extensions.Options.Options.Create(new AirlockOptions()),
            TimeProvider.System,
            new FakeLogger<SyftSbomGenerator>());
}
