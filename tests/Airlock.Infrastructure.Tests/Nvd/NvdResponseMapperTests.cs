using System.Text.Json;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Text;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Tests.Support;

namespace Airlock.Infrastructure.Tests.Nvd;

// IR-09 — the metric table, sources, products, rejected records and descriptions; defect 11 (CVSS parsing).
public sealed class NvdResponseMapperTests
{
    [Fact]
    public void Pyyamls_two_CVEs_map_with_every_score_and_their_products()
    {
        var records = Map("cpe-pyyaml-5.3");

        Assert.Equal(["CVE-2020-1747", "CVE-2020-14343"], records.Select(record => record.Id));
        var cve = records[0];
        Assert.Equal(
            [
                new CvssScore(9.8m, Severity.Critical, CvssVersion.V31, CvssSource.Secondary),
                new CvssScore(9.8m, Severity.Critical, CvssVersion.V31, CvssSource.Primary),
                new CvssScore(10.0m, Severity.High, CvssVersion.V20, CvssSource.Primary),
            ],
            cve.Scores);
        Assert.Equal(CvssSource.Primary, cve.PreferredScore!.Source);
        Assert.Contains("pyyaml:pyyaml", cve.AffectedProducts);
        Assert.Equal(new Uri("https://nvd.nist.gov/vuln/detail/CVE-2020-1747"), cve.NvdUrl);
    }

    [Fact]
    public void Primary_and_secondary_scores_can_disagree_and_both_are_kept()
    {
        var cve = Map("records-requests-keyword").Single(record => record.Id == "CVE-2023-29530");

        Assert.Contains(new CvssScore(6.5m, Severity.Medium, CvssVersion.V31, CvssSource.Primary), cve.Scores);
        Assert.Contains(new CvssScore(7.5m, Severity.High, CvssVersion.V31, CvssSource.Secondary), cve.Scores);
        Assert.Equal(6.5m, cve.PreferredScore!.BaseScore);
    }

    [Fact]
    public void A_CVE_scored_only_in_v3_0_keeps_its_HIGH()
    {
        var cve = Map("cpe-flask-0.12.2").Single(record => record.Id == "CVE-2018-1000656");

        Assert.Equal(new CvssScore(7.5m, Severity.High, CvssVersion.V30, CvssSource.Primary), cve.PreferredScore);
        Assert.Contains(new CvssScore(5.0m, Severity.Medium, CvssVersion.V20, CvssSource.Primary), cve.Scores);
    }

    [Fact]
    public void CVSS_v4_scores_are_read_next_to_v3_1()
    {
        var cve = Map("cpe-flask-0.12.2").Single(record => record.Id == "CVE-2026-27205");

        Assert.Contains(new CvssScore(2.3m, Severity.Low, CvssVersion.V40, CvssSource.Secondary), cve.Scores);
        Assert.Equal(new CvssScore(4.3m, Severity.Medium, CvssVersion.V31, CvssSource.Primary), cve.PreferredScore);
    }

    [Fact]
    public void Other_metric_kinds_such_as_SSVC_are_ignored()
    {
        var cve = Map("records-requests-keyword").Single(record => record.Id == "CVE-2023-29530");

        Assert.Equal(2, cve.Scores.Count);
    }

    [Fact]
    public void Affected_products_come_from_vulnerable_criteria_once_each()
    {
        var cve = Map("cve-2024-36401").Single();

        Assert.Equal(["geoserver:geoserver", "geotools:geotools"], cve.AffectedProducts.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Rejected_records_are_dropped()
    {
        var records = NvdResponseMapper.Map(Page(Record("CVE-2026-0001", status: "Rejected"), Record("CVE-2026-0002")));

        Assert.Equal("CVE-2026-0002", Assert.Single(records).Id);
    }

    [Fact]
    public void A_missing_severity_is_derived_from_the_score()
    {
        var cve = Assert.Single(NvdResponseMapper.Map(Page(Record(
            "CVE-2026-0003",
            metrics: "{\"cvssMetricV31\":[{\"type\":\"Primary\",\"cvssData\":{\"baseScore\":9.8}}],"
                + "\"cvssMetricV2\":[{\"type\":\"Primary\",\"cvssData\":{\"baseScore\":7.5}}]}"))));

        Assert.Contains(new CvssScore(9.8m, Severity.Critical, CvssVersion.V31, CvssSource.Primary), cve.Scores);
        Assert.Contains(new CvssScore(7.5m, Severity.High, CvssVersion.V20, CvssSource.Primary), cve.Scores);
    }

    [Theory]
    [InlineData("{\"vulnerabilities\":[{\"cve\":{\"id\":\"NOT-A-CVE\"}}]}")]
    [InlineData("{\"vulnerabilities\":[{}]}")]
    [InlineData("{\"vulnerabilities\":[{\"cve\":{\"id\":\"CVE-2026-0004\",\"metrics\":{\"cvssMetricV31\":[{\"type\":\"Primary\"}]}}}]}")]
    [InlineData("{\"vulnerabilities\":[{\"cve\":{\"id\":\"CVE-2026-0005\",\"metrics\":{\"cvssMetricV31\":[{\"cvssData\":{\"baseScore\":12.5}}]}}}]}")]
    [InlineData("{\"vulnerabilities\":[{\"cve\":{\"id\":\"CVE-2026-0006\",\"configurations\":[{\"nodes\":[{\"cpeMatch\":[{\"vulnerable\":true,\"criteria\":\"nonsense\"}]}]}]}}]}")]
    public void A_record_the_Domain_rejects_fails_the_whole_page(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => NvdResponseMapper.Map(Parse(json)));
    }

    [Fact]
    public void Descriptions_lose_control_and_bidirectional_characters_but_keep_line_feeds_and_tabs()
    {
        var text = "one\r\ntwo\tthree" + (char)0x1B + "[31mred" + (char)0x202E + "reversed" + (char)0x2066 + "!";

        Assert.Equal("one\ntwo\tthree[31mredreversed!", NvdResponseMapper.Description([new NvdDescription("en", text)]));
    }

    [Fact]
    public void Only_the_English_description_is_used()
    {
        Assert.Equal("hello", NvdResponseMapper.Description([new NvdDescription("es", "hola"), new NvdDescription("en", "hello")]));
        Assert.Null(NvdResponseMapper.Description([new NvdDescription("es", "hola")]));
        Assert.Null(NvdResponseMapper.Description([new NvdDescription("en", "  ")]));
        Assert.Null(NvdResponseMapper.Description(null));
    }

    [Fact]
    public void Descriptions_are_cut_to_16384_characters_without_splitting_a_surrogate_pair()
    {
        var text = new string('a', DomainLimits.CveDescription - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        var cut = NvdResponseMapper.Description([new NvdDescription("en", text)])!;

        Assert.Equal(DomainLimits.CveDescription - 1, cut.Length);
        Assert.False(char.IsSurrogate(cut[^1]));
    }

    private static IReadOnlyList<Cve> Map(string fixture) =>
        NvdResponseMapper.Map(Parse(File.ReadAllText(Fixtures.NvdBody(fixture))));

    private static NvdCveResponse Parse(string json) =>
        JsonSerializer.Deserialize(json, NvdJsonContext.Default.NvdCveResponse) ?? throw new InvalidOperationException("No page.");

    private static NvdCveResponse Page(params string[] records) =>
        Parse("{\"resultsPerPage\":" + records.Length + ",\"startIndex\":0,\"totalResults\":" + records.Length
            + ",\"vulnerabilities\":[" + string.Join(",", records) + "]}");

    private static string Record(string id, string status = "Analyzed", string metrics = "{}") =>
        "{\"cve\":{\"id\":\"" + id + "\",\"vulnStatus\":\"" + status + "\",\"descriptions\":[{\"lang\":\"en\",\"value\":\"A test record.\"}],"
        + "\"metrics\":" + metrics + ",\"configurations\":[]}}";
}
