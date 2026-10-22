using Airlock.Application.Ports;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Scanning;

// AR-08 to AR-12; defects 6 and 13.
public sealed class SbomGenerationTests
{
    [Fact]
    public async Task A_directory_target_is_one_run_over_its_full_path()
    {
        using var harness = new ScanHarness();

        await harness.RunDirectoryScanAsync();

        Assert.Equal(new DirectorySource(ScanHarness.Directory), Assert.Single(harness.Sbom.Calls));
    }

    [Fact]
    public async Task Manifests_share_one_run_and_other_artifacts_run_on_their_own_in_upload_order()
    {
        using var harness = new ScanHarness();

        await harness.RunUploadScanAsync("app-image.tar", "requirements.txt", "notes.docx", "package-lock.json");

        var calls = harness.Sbom.Calls;
        Assert.Equal(3, calls.Count);
        Assert.EndsWith("/" + FakeSbomGenerator.Manifests, Assert.IsType<DirectorySource>(calls[0]).Path, StringComparison.Ordinal);
        Assert.Equal("app-image.tar", Assert.IsType<FileSource>(calls[1]).Artifact.FileName);
        Assert.Equal("notes.docx", Assert.IsType<FileSource>(calls[2]).Artifact.FileName);
    }

    [Fact]
    public async Task A_failed_run_fails_the_scan_naming_its_input_and_the_later_runs_are_not_started()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Fails("first.tar", new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1"));

        var scan = await harness.RunUploadScanAsync("first.tar", "second.tar");

        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureCode.SyftFailed, scan.Failure!.Code);
        Assert.Equal("first.tar", scan.Failure.Input);
        Assert.Null(scan.Summary);
        Assert.Single(harness.Sbom.Calls);
    }

    [Fact]
    public async Task Syft_failing_on_a_directory_is_never_reported_clean()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Fails(
            ScanHarness.Directory,
            new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1 for /uploads/acme-app", ScanHarness.Directory));

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanHarness.Directory, scan.Failure!.Input);
        Assert.Contains(ScanHarness.Directory, scan.Failure.Reason, StringComparison.Ordinal);
        Assert.Null(scan.Summary);
        Assert.Contains(harness.LogRecords, record => record.Message == "❌ Syft failed: Syft exited with code 1 for /uploads/acme-app");
    }

    [Fact]
    public async Task A_file_without_packages_adds_a_warning_and_does_not_fail_the_scan()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Produces(FakeSbomGenerator.Manifests, A.SbomPackage(A.Package("pyyaml", "5.3")));

        var scan = await harness.RunUploadScanAsync("requirements.txt", "notes.docx");

        var warning = Assert.Single(scan.Warnings);
        Assert.Equal(ScanWarningCode.NoPackagesIdentified, warning.Code);
        Assert.Equal("No packages identified in notes.docx", warning.Message);
        Assert.Equal("notes.docx", warning.Input);
        Assert.Equal(ScanStatus.Completed, scan.Status);
    }

    [Fact]
    public async Task An_empty_manifests_run_names_every_manifest_and_an_empty_directory_names_its_path()
    {
        using var uploads = new ScanHarness();
        var uploadScan = await uploads.RunUploadScanAsync("requirements.txt", "package-lock.json");
        using var directory = new ScanHarness();
        var directoryScan = await directory.RunDirectoryScanAsync();

        Assert.Equal("No packages identified in requirements.txt, package-lock.json", Assert.Single(uploadScan.Warnings).Message);
        Assert.Equal("No packages identified in /uploads/acme-app", Assert.Single(directoryScan.Warnings).Message);
        Assert.Equal(0, directoryScan.Summary!.TotalPackages);
    }

    [Fact]
    public async Task A_container_image_adds_the_backports_warning()
    {
        using var harness = new ScanHarness();
        harness.Sbom.ProducesImage("alpine-3.20.tar", A.SbomPackage(A.Package("busybox", "1.36.1-r31", "apk")));

        var scan = await harness.RunUploadScanAsync("alpine-3.20.tar");

        var warning = Assert.Single(scan.Warnings);
        Assert.Equal(ScanWarningCode.DistributionBackportsNotConsidered, warning.Code);
        Assert.Equal("alpine-3.20.tar", warning.Input);
        Assert.StartsWith("Distribution backports are not considered for alpine-3.20.tar", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Packages_are_merged_by_identity_keeping_the_first_spelling_and_every_candidate()
    {
        using var harness = new ScanHarness();
        harness.Sbom
            .Produces(
                FakeSbomGenerator.Manifests,
                A.SbomPackage(A.Package("PyYAML", "5.3"), A.Cpe("python:pyyaml", "5.3")),
                A.SbomPackage(A.Package("requests", "2.25.1")))
            .ProducesImage(
                "app-image.tar",
                A.SbomPackage(A.Package("pyyaml", "5.3"), A.Cpe("pyyaml:pyyaml", "5.3")),
                A.SbomPackage(A.Package("requests", "2.25.1")));
        harness.Catalog.Cpe(A.CpeValue("pyyaml:pyyaml", "5.3"), A.Cve("CVE-2020-1747", 9.8m, Severity.Critical));

        var scan = await harness.RunUploadScanAsync("requirements.txt", "app-image.tar");

        Assert.Equal(2, scan.Summary!.TotalPackages);
        var pyyaml = Assert.Single(scan.VulnerablePackages);
        Assert.Equal("PyYAML", pyyaml.Package.Name);
        Assert.Equal(A.CpeValue("pyyaml:pyyaml", "5.3"), Assert.IsType<CpeMatch>(pyyaml.Method).Cpe.Value);
    }
}
