namespace Airlock.Web.Api;

/// <summary>The fixed values of the U4 NFR design (nfr-design-patterns.md, values chosen).</summary>
internal static class ApiLimits
{
    /// <summary>Kestrel's limit for every request but the submission, which raises its own (U4-NFR-03).</summary>
    public const long ServerBodyLimit = 1024 * 1024;

    /// <summary>What the submission may carry beyond <c>Limits.MaxBytesPerScan</c> for its multipart framing.</summary>
    public const long FramingAllowance = 1024 * 1024;

    /// <summary>The most of a <c>targetPath</c> value that is read: 4,096 characters at up to 4 bytes each (WR-07).</summary>
    public const int TargetPathReadLimit = 16 * 1024;

    /// <summary>RFC 2046's limit for a multipart boundary.</summary>
    public const int MaxBoundaryLength = 70;

    /// <summary>How much of an unknown field's name a refusal shows (WR-07).</summary>
    public const int FieldNameShown = 64;

    /// <summary>A read of the submission that waits this long for data ends the upload (U4-NFR-04).</summary>
    public static readonly TimeSpan UploadIdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>A stream silent this long receives a keep-alive comment (U4-NFR-05).</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    /// <summary>How long a stream may go on once the host begins to stop (U4-NFR-05).</summary>
    public static readonly TimeSpan StreamShutdownGrace = TimeSpan.FromSeconds(1);
}
