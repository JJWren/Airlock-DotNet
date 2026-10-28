using System.Globalization;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.UseCases;

namespace Airlock.Application.Tests.UseCases;

// U5 UR-08: the page's checks before uploading use the reservation's own refusals.
public sealed class UploadLimitsTests
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(25, 2 * Gibibyte)]
    public void Files_within_the_limits_are_not_refused(int fileCount, long totalBytes)
    {
        Assert.Null(UploadLimits.Check(fileCount, totalBytes, new LimitsOptions()));
    }

    [Fact]
    public void Too_many_files_are_refused_first()
    {
        var refusal = UploadLimits.Check(26, 3 * Gibibyte, new LimitsOptions());

        Assert.Equal(new Refusal(RefusalCode.TooManyArtifacts, "At most 25 files per scan"), refusal);
    }

    [Fact]
    public void Too_many_bytes_are_refused()
    {
        var refusal = UploadLimits.Check(1, (2 * Gibibyte) + 1, new LimitsOptions());

        Assert.Equal(new Refusal(RefusalCode.TooLarge, "Uploads are limited to 2 GB per scan"), refusal);
    }

    [Theory]
    [InlineData(2 * Gibibyte, "2 GB")]
    [InlineData(Gibibyte, "1 GB")]
    [InlineData(3L * 1024 * 1024, "3 MB")]
    [InlineData(1536L * 1024, "1.5 MB")]
    [InlineData(1536L * 1024 * 1024, "1536 MB")]
    public void Sizes_are_worded_in_whole_gibibytes_or_in_mebibytes(long bytes, string expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(expected, UploadLimits.Size(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task The_reservation_refuses_in_the_same_words()
    {
        using var harness = new ScanHarness(configure: options => options.Limits.MaxArtifactsPerScan = 1);
        await using var reservation = await harness.ReserveAsync();
        using (var first = new MemoryStream([1]))
        {
            Assert.True((await reservation.AddArtifactAsync("first.bin", first, harness.Token)).IsSuccess);
        }

        using var second = new MemoryStream([1]);
        var refused = await reservation.AddArtifactAsync("second.bin", second, harness.Token);

        Assert.Equal(UploadLimits.Check(2, 2, harness.Options.Limits), refused.Refusal);
    }
}