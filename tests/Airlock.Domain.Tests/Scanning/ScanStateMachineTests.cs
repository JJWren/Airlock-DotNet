using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests.Scanning;

// BR-14, BR-15, BR-16
public sealed class ScanStateMachineTests
{
    private static readonly ScanWarning Backports =
        new(ScanWarningCode.DistributionBackportsNotConsidered, "Distribution backports are not considered.");

    [Fact]
    public void Starting_a_scan_makes_it_run_in_SBOM_generation_with_a_UTC_start_time()
    {
        var id = ScanId.New();
        var target = new DirectoryTarget("/uploads");

        var scan = Scan.Start(id, target, new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(id, scan.Id);
        Assert.Same(target, scan.Target);
        Assert.Equal(ScanStatus.Running, scan.Status);
        Assert.Equal(ScanPhase.SbomGeneration, scan.Phase);
        Assert.Equal(A.ScanStart, scan.StartedAt);
        Assert.Equal(TimeSpan.Zero, scan.StartedAt.Offset);
        Assert.False(scan.IsFinished);
        Assert.Null(scan.Duration);
        Assert.Null(scan.Progress);
        Assert.Null(scan.Summary);
        Assert.Empty(scan.VulnerablePackages);
        Assert.Empty(scan.UnverifiedPackages);
        Assert.Empty(scan.Warnings);
    }

    [Fact]
    public void A_scan_needs_an_id_and_a_target()
    {
        Assert.Throws<ArgumentException>(() => Scan.Start(default, new DirectoryTarget("/uploads"), A.ScanStart));
        Assert.Throws<ArgumentNullException>(() => Scan.Start(ScanId.New(), null!, A.ScanStart));
    }

    [Fact]
    public void Phases_only_move_forward()
    {
        var scan = A.RunningScan().EnterPhase(ScanPhase.Correlation).EnterPhase(ScanPhase.Summary);

        Assert.Equal(ScanPhase.Summary, scan.Phase);
        Assert.Throws<InvalidOperationException>(() => scan.EnterPhase(ScanPhase.Correlation));
        Assert.Throws<InvalidOperationException>(() => scan.EnterPhase(ScanPhase.Summary));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.RunningScan().EnterPhase((ScanPhase)42));
    }

    [Fact]
    public void Progress_is_recorded_only_during_correlation_and_never_goes_backwards()
    {
        var scan = A.ScanInCorrelation().RecordProgress(0, 87).RecordProgress(40, 87);

        Assert.Equal(new ScanProgress(40, 87), scan.Progress);
        Assert.Throws<InvalidOperationException>(() => A.RunningScan().RecordProgress(1, 2));
        Assert.Throws<ArgumentException>(() => scan.RecordProgress(39, 87));
        Assert.Throws<ArgumentException>(() => scan.RecordProgress(41, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => scan.RecordProgress(88, 87));
    }

    [Fact]
    public void Warnings_are_added_while_running()
    {
        var scan = A.RunningScan().AddWarning(Backports);

        Assert.Equal([Backports], scan.Warnings);
        Assert.Throws<ArgumentNullException>(() => scan.AddWarning(null!));
    }

    [Fact]
    public void Finishing_without_unverified_packages_completes_the_scan()
    {
        var vulnerable = A.Vulnerable(A.Package(), A.Finding("CVE-2020-1747", 9.8m, Severity.Critical));

        var scan = A.ScanInCorrelation().Finish(10, [vulnerable], [], A.ScanStart.AddMinutes(2));

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.True(scan.IsFinished);
        Assert.Equal(A.ScanStart.AddMinutes(2), scan.FinishedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), scan.Duration);
        Assert.Equal(9, scan.Summary!.CleanPackages);
        Assert.Equal([vulnerable], scan.VulnerablePackages);
    }

    [Fact]
    public void Finishing_with_unverified_packages_makes_the_scan_incomplete()
    {
        var scan = A.ScanInCorrelation().Finish(10, [], [A.Unverified(A.Package())], A.ScanStart.AddMinutes(1));

        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        Assert.Equal(1, scan.Summary!.UnverifiedPackages);
    }

    [Fact]
    public void A_scan_with_an_empty_SBOM_can_finish_without_correlating()
    {
        var scan = A.RunningScan().Finish(0, [], [], A.ScanStart);

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(0, scan.Summary!.TotalPackages);
    }

    [Fact]
    public void Results_are_ordered_by_risk_then_name()
    {
        var high = A.Vulnerable(A.Package("zlib", "1.0"), A.Finding("CVE-2020-0001", 7.5m, Severity.High));
        var critical = A.Vulnerable(A.Package("yaml", "1.0"), A.Finding("CVE-2020-0002", 9.8m, Severity.Critical));
        var alsoHigh = A.Vulnerable(A.Package("attrs", "1.0"), A.Finding("CVE-2020-0003", 7.5m, Severity.High));

        var scan = A.ScanInCorrelation().Finish(
            10, [high, critical, alsoHigh], [A.Unverified(A.Package("b", "1")), A.Unverified(A.Package("a", "1"))], A.ScanStart);

        Assert.Equal(["yaml", "attrs", "zlib"], scan.VulnerablePackages.Select(package => package.Package.Name));
        Assert.Equal(["a", "b"], scan.UnverifiedPackages.Select(package => package.Package.Name));
    }

    [Fact]
    public void Failing_records_the_failure_and_no_results()
    {
        var failure = new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1.", "/uploads/acme-app");

        var scan = A.RunningScan().Fail(failure, A.ScanStart.AddSeconds(5));

        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Same(failure, scan.Failure);
        Assert.Null(scan.Summary);
        Assert.Equal(TimeSpan.FromSeconds(5), scan.Duration);
        Assert.Throws<ArgumentNullException>(() => A.RunningScan().Fail(null!, A.ScanStart));
    }

    [Fact]
    public void Cancelling_records_the_reason_and_no_results()
    {
        var scan = A.ScanInCorrelation().Cancel("Emergency Stop", A.ScanStart.AddSeconds(30));

        Assert.Equal(ScanStatus.Cancelled, scan.Status);
        Assert.Equal("Emergency Stop", scan.CancellationReason);
        Assert.Null(scan.Summary);
        Assert.Throws<ArgumentException>(() => A.RunningScan().Cancel(" ", A.ScanStart));
    }

    [Fact]
    public void A_finished_scan_accepts_no_more_transitions()
    {
        var finished = A.RunningScan().Finish(0, [], [], A.ScanStart);

        var exception = Assert.Throws<InvalidOperationException>(() => finished.Finish(0, [], [], A.ScanStart));
        Assert.Equal("Cannot finish a scan that is Completed in phase SbomGeneration.", exception.Message);
        Assert.Throws<InvalidOperationException>(() => finished.EnterPhase(ScanPhase.Correlation));
        Assert.Throws<InvalidOperationException>(() => finished.RecordProgress(1, 1));
        Assert.Throws<InvalidOperationException>(() => finished.AddWarning(Backports));
        Assert.Throws<InvalidOperationException>(() => finished.Fail(new ScanFailure(ScanFailureCode.Unexpected, "x"), A.ScanStart));
        Assert.Throws<InvalidOperationException>(() => finished.Cancel("x", A.ScanStart));
    }

    [Fact]
    public void A_scan_cannot_end_before_it_started()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => A.RunningScan().Finish(0, [], [], A.ScanStart.AddSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.RunningScan().Cancel("x", A.ScanStart.AddSeconds(-1)));
    }

    [Fact]
    public void Finishing_rejects_missing_or_inconsistent_results()
    {
        Assert.Throws<ArgumentNullException>(() => A.RunningScan().Finish(0, null!, [], A.ScanStart));
        Assert.Throws<ArgumentNullException>(() => A.RunningScan().Finish(0, [], null!, A.ScanStart));
        Assert.Throws<ArgumentException>(() => A.RunningScan().Finish(0, [null!], [], A.ScanStart));
        Assert.Throws<ArgumentException>(() => A.RunningScan().Finish(0, [], [null!], A.ScanStart));
        Assert.Throws<ArgumentException>(() => A.RunningScan().Finish(0, [], [A.Unverified(A.Package())], A.ScanStart));
    }
}
