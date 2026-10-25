using System.Net;
using Airlock.Domain.Scanning;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Endpoints;

// WR-14 — the PDF of a finished scan as an attachment; none while running, failed or cancelled (E11; defect 18).
public sealed class ReportEndpointTests
{
    [Fact]
    public async Task A_finished_scans_report_is_a_pdf_attachment()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, A.Sbom("pyyaml", "5.3"));
        factory.Catalog.Cpe(A.CpeFor("pyyaml", "5.3"), A.Cve("CVE-2020-1747"));
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/report.pdf", Test.Token);
        var pdf = await response.Content.ReadAsByteArrayAsync(Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Matches("^airlock_audit_[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}-[0-9]{2}-[0-9]{2}-[0-9]{3}Z\\.pdf$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }

    [Fact]
    public async Task A_running_scans_report_is_not_ready()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/report.pdf", Test.Token);

        Assert.Equal("The scan is still running", (await response.ProblemAsync(HttpStatusCode.Conflict, "scan-not-finished")).Detail());
        using var stop = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        await client.WaitForEndAsync(id);
    }

    [Fact]
    public async Task Failed_and_cancelled_scans_have_no_report()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Fails(FakeSbomGenerator.Manifests, new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1: no"));
        using var client = factory.CreateClient();
        var failed = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(failed);
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        var cancelled = await client.StartScanWhenFreeAsync(Http.Requirements);
        using (var cancel = await client.PostAsync($"{Http.ScansUrl}/{cancelled}/cancel", null, Test.Token))
        {
            await client.WaitForEndAsync(cancelled);
        }

        foreach (var id in new[] { failed, cancelled })
        {
            using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/report.pdf", Test.Token);
            Assert.Equal("The scan ended without results", (await response.ProblemAsync(HttpStatusCode.Conflict, "scan-has-no-results")).Detail());
        }
    }
}
