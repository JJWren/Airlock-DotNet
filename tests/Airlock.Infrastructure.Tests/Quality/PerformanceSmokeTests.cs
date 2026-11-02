using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Reports;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Quality;

// U3-NFR-10 at ten times its targets (Build and Test measures the targets themselves), and U3-NFR-05's flat memory.
[Collection(AllocationMeasurements.Name)]
public sealed class PerformanceSmokeTests
{
    [Fact]
    public async Task Syfts_281_package_output_parses_well_inside_a_second()
    {
        await ParseAsync(File.ReadAllBytes(Fixtures.SyftOutput("gobin-file")));

        var elapsed = await TimeAsync(async () => Assert.Equal(281, await ParseAsync(File.ReadAllBytes(Fixtures.SyftOutput("gobin-file")))));

        Measured("U3-NFR-10", $"Syft's 281-package output parsed in {elapsed.TotalMilliseconds:0.0} ms (target 100 ms)");
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed}.");
    }

    [Fact]
    public async Task A_10000_package_output_parses_well_inside_20_seconds()
    {
        var json = new StringBuilder("{\"source\":{\"type\":\"directory\"},\"artifacts\":[");
        for (var i = 0; i < 10_000; i++)
        {
            json.Append(i == 0 ? string.Empty : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"type\":\"npm\",\"name\":\"package-{i}\",\"version\":\"1.{i}.0\",\"cpes\":[{{\"cpe\":\"cpe:2.3:a:package-{i}:package-{i}:1.{i}.0:*:*:*:*:*:*:*\",\"source\":\"syft-generated\"}}],\"metadata\":{{\"integrity\":\"sha512-x\",\"resolved\":\"https://registry.npmjs.org/package-{i}\"}}}}");
        }

        json.Append("]}");
        var bytes = Encoding.UTF8.GetBytes(json.ToString());

        var elapsed = await TimeAsync(async () => Assert.Equal(10_000, await ParseAsync(bytes)));

        Measured("U3-NFR-10", $"a 10,000-package Syft output parsed in {elapsed.TotalMilliseconds:0.0} ms (target 2,000 ms)");
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"Took {elapsed}.");
    }

    [Fact]
    public void A_2000_record_NVD_page_maps_well_inside_10_seconds()
    {
        var record = JsonDocument.Parse(File.ReadAllText(Fixtures.NvdBody("cpe-pyyaml-5.3"))).RootElement.GetProperty("vulnerabilities")[0].GetRawText();
        var page = "{\"resultsPerPage\":2000,\"startIndex\":0,\"totalResults\":2000,\"vulnerabilities\":["
            + string.Join(",", Enumerable.Range(0, 2_000).Select(i => record.Replace("CVE-2020-1747", $"CVE-2026-{10_000 + i}", StringComparison.Ordinal)))
            + "]}";

        var stopwatch = Stopwatch.StartNew();
        var records = NvdResponseMapper.Map(JsonSerializer.Deserialize(page, NvdJsonContext.Default.NvdCveResponse)!);
        stopwatch.Stop();

        Assert.Equal(2_000, records.Count);
        Measured("U3-NFR-10", $"a 2,000-record NVD page mapped in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms (target 1,000 ms)");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_500_finding_report_renders_well_inside_50_seconds()
    {
        var renderer = new QuestPdfScanReportRenderer(new FakeTimeProvider(Test.StartTime), new FakeLogger<QuestPdfScanReportRenderer>());
        var scan = A.LargeScan(500);

        var elapsed = await TimeAsync(async () => Assert.NotEmpty(await renderer.RenderPdfAsync(scan, Test.Token)));

        Measured("U3-NFR-10", $"a 500-finding report rendered in {elapsed.TotalMilliseconds:0.0} ms (target 5,000 ms)");
        Assert.True(elapsed < TimeSpan.FromSeconds(50), $"Took {elapsed}.");
    }

    [Fact]
    public async Task Staging_a_256_MiB_upload_allocates_under_16_MB()
    {
        using var folder = new TemporaryFolder();
        var staging = new DiskArtifactStaging(
            Microsoft.Extensions.Options.Options.Create(new StagingOptions { Root = folder.Path }), TimeProvider.System, NullLogger<DiskArtifactStaging>.Instance);
        await using var area = await staging.CreateAsync(ScanId.New(), Test.Token);
        await using var content = new GeneratedStream(256L * 1024 * 1024);

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var outcome = await area.AddAsync("big.tar", content, long.MaxValue, Test.Token);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(256L * 1024 * 1024, Assert.IsType<ArtifactStaged>(outcome).Artifact.Artifact.SizeBytes);
        Measured("U3-NFR-05", $"staging a 256 MiB upload allocated {allocated / 1024.0 / 1024.0:0.0} MiB (target under 16 MB)");
        Assert.True(allocated < 16L * 1024 * 1024, $"Allocated {allocated:N0} bytes.");
    }

    // Build and Test reads each measurement from the test results (refinement 1 of its plan).
    private static void Measured(string requirement, FormattableString measurement) =>
        TestContext.Current.TestOutputHelper?.WriteLine($"measured {requirement}: " + measurement.ToString(CultureInfo.InvariantCulture));

    private static async Task<int> ParseAsync(byte[] json)
    {
        using var stream = new MemoryStream(json);
        var parse = await SyftJsonParser.ParseAsync(stream, Test.Token);
        return SyftJsonParser.Map(parse.Document!).Packages.Count;
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> work)
    {
        var stopwatch = Stopwatch.StartNew();
        await work();
        return stopwatch.Elapsed;
    }

    /// <summary>A stream of the given length that produces its bytes without holding them.</summary>
    private sealed class GeneratedStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = (int)Math.Min(buffer.Length, length - position);
            buffer[..read].Fill(0x41);
            position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Tests that measure the process's allocations run on their own, after the others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurements
{
    public const string Name = "Allocation measurements";
}
