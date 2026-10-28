using System.Globalization;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;

namespace Airlock.Application.UseCases;

/// <summary>
/// The reservation's refusals for a scan's file count and bytes, worded once so that the page's checks before uploading
/// and the API's answers cannot differ (U5 UR-08).
/// </summary>
public static class UploadLimits
{
    private const long Mebibyte = 1024 * 1024;
    private const long Gibibyte = 1024 * Mebibyte;

    /// <summary>The refusal for <paramref name="fileCount"/> files of <paramref name="totalBytes"/> bytes in all, or null.</summary>
    public static Refusal? Check(int fileCount, long totalBytes, LimitsOptions limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (fileCount > limits.MaxArtifactsPerScan)
        {
            return TooManyArtifacts(limits);
        }

        return totalBytes > limits.MaxBytesPerScan ? TooLarge(limits) : null;
    }

    /// <summary>The refusals' size wording: whole gibibytes as "2 GB", anything else in mebibytes, such as "1.5 MB".</summary>
    public static string Size(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return bytes % Gibibyte == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Gibibyte} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Mebibyte:0.#} MB");
    }

    // AR-06: the reservation checks the count before each file, and the bytes while each one streams.
    internal static Refusal TooManyArtifacts(LimitsOptions limits) =>
        new(RefusalCode.TooManyArtifacts, string.Create(CultureInfo.InvariantCulture, $"At most {limits.MaxArtifactsPerScan} files per scan"));

    internal static Refusal TooLarge(LimitsOptions limits) =>
        new(RefusalCode.TooLarge, $"Uploads are limited to {Size(limits.MaxBytesPerScan)} per scan");
}