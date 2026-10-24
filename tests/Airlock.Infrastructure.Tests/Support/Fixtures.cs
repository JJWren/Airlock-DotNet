namespace Airlock.Infrastructure.Tests.Support;

/// <summary>The recorded S0 outputs, copied next to the tests (U3-NFR-12).</summary>
internal static class Fixtures
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>A recorded Syft output, such as <c>manifest-file</c> (S1) or <c>gobin-file</c> (S10).</summary>
    public static string SyftOutput(string name) => Path.Combine(Root, "Syft", name + ".json");

    /// <summary>A recorded NVD answer's body, such as <c>cpe-pyyaml-5.3</c> (N3).</summary>
    public static string NvdBody(string name) => Path.Combine(Root, "Nvd", name + ".json");

    /// <summary>The recorded answer's status line and the headers kept: Content-Type, Retry-After, message.</summary>
    public static string NvdHeaders(string name) => Path.Combine(Root, "Nvd", name + ".headers");
}
