using System.Text;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-06, E13 — with a key configured and Debug logging, a full scan through the API shows the key nowhere.
public sealed class SecretHygieneTests
{
    private const string Key = "AIRLOCK-SECRET-KEY-7f3e9a1c";

    [Fact]
    public async Task The_nvd_key_appears_in_no_answer_no_event_and_no_log_line()
    {
        await using var factory = new AirlockWebFactory(settings => settings["Airlock:Nvd:ApiKey"] = Key);
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, A.Sbom("pyyaml", "5.3"), A.Sbom("jinja2", "2.10"));
        factory.Catalog.Cpe(A.CpeFor("pyyaml", "5.3"), A.Cve("CVE-2020-1747"));
        factory.Catalog.FailsFor(A.CpeFor("jinja2", "2.10"), "NVD returned 503");
        using var client = factory.CreateClient();
        var seen = new StringBuilder();

        var id = await client.StartScanAsync(Http.Requirements());
        seen.Append((await client.WaitForEndAsync(id)).ToString());
        foreach (var url in new[] { "/api/v2/system/health", $"{Http.ScansUrl}/{id}/events", $"{Http.ScansUrl}/{Guid.NewGuid()}", "/openapi/v2.json", "/docs/" })
        {
            using var response = await client.GetAsync(url, Test.Token);
            seen.Append(await response.Content.ReadAsStringAsync(Test.Token));
        }

        using (var refused = await client.PostAsync(Http.ScansUrl, new StringContent("{}", Encoding.UTF8, "application/json"), Test.Token))
        {
            seen.Append(await refused.Content.ReadAsStringAsync(Test.Token));
        }

        using (var logs = await client.GetAsync("/api/v2/logs/stream", HttpCompletionOption.ResponseHeadersRead, Test.Token))
        using (var events = new SseReader(await logs.Content.ReadAsStreamAsync(Test.Token)))
        {
            while (await events.NextAsync(Test.Token).Within() is { } line)
            {
                seen.Append(line.Data);
                if (line.Data.Contains("not multipart/form-data", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        foreach (var record in factory.Logs.GetSnapshot())
        {
            seen.Append(record.Message).Append(record.Exception);
            foreach (var pair in record.StructuredState ?? [])
            {
                seen.Append(pair.Key).Append('=').Append(pair.Value);
            }
        }

        Assert.DoesNotContain(Key, seen.ToString(), StringComparison.Ordinal);
        Assert.Contains("CVE-2020-1747", seen.ToString(), StringComparison.Ordinal);
    }
}
