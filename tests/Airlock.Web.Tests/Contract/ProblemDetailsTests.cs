using System.Net;
using System.Text.RegularExpressions;
using Airlock.Application.Outcomes;
using Airlock.Web.Api.Problems;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Tests.Contract;

// WR-03 to WR-05 — every error is a problem of the catalogue, with five members and nothing internal.
public sealed partial class ProblemDetailsTests
{
    [Theory]
    [InlineData(RefusalCode.ScanInProgress, 409, "scan-in-progress")]
    [InlineData(RefusalCode.NotReady, 503, "not-ready")]
    [InlineData(RefusalCode.InvalidTarget, 400, "invalid-target")]
    [InlineData(RefusalCode.TooManyArtifacts, 400, "too-many-artifacts")]
    [InlineData(RefusalCode.TooLarge, 413, "too-large")]
    [InlineData(RefusalCode.ReservationStopped, 409, "upload-stopped")]
    [InlineData(RefusalCode.ScanNotFound, 404, "scan-not-found")]
    [InlineData(RefusalCode.ScanNotFinished, 409, "scan-not-finished")]
    [InlineData(RefusalCode.ScanAlreadyFinished, 409, "scan-already-finished")]
    [InlineData(RefusalCode.ScanHasNoResults, 409, "scan-has-no-results")]
    public void Every_refusal_has_its_status_and_kind(RefusalCode code, int status, string slug)
    {
        var kind = ApiProblems.For(code);

        Assert.Equal(status, kind.Status);
        Assert.Equal(slug, kind.Slug);
    }

    [Fact]
    public void Every_refusal_code_is_mapped()
    {
        foreach (var code in Enum.GetValues<RefusalCode>())
        {
            Assert.Contains(ApiProblems.For(code), ApiProblems.All);
        }
    }

    [Fact]
    public void The_problems_page_describes_exactly_the_catalogue()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "api", "problems.md"));

        var headings = Heading().Matches(page).Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(ApiProblems.All.Select(kind => kind.Slug), headings);
        foreach (var kind in ApiProblems.All)
        {
            Assert.Contains($"**{kind.Status}** · {kind.Title}.", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_unknown_address_answers_not_found()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/scan", Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.NotFound, "not-found");
        Assert.Equal("No API endpoint at this address", problem.Detail());
        AssertFiveMembers(problem);
    }

    [Fact]
    public async Task A_wrong_method_answers_method_not_allowed_with_allow()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Http.ScansUrl, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.MethodNotAllowed, "method-not-allowed");
        Assert.Equal("This endpoint does not accept GET", problem.Detail());
        Assert.Contains("POST", response.Content.Headers.Allow);
        AssertFiveMembers(problem);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task An_unexpected_exception_answers_500_without_any_of_its_details(string environment)
    {
        await using var factory = new AirlockWebFactory(environment: environment);
        factory.Sbom.StatusFailure = new InvalidOperationException("secret-looking detail at C:/internal/airlock/staging");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/system/health", Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.InternalServerError, "unexpected-error");
        var body = problem.ToString();
        Assert.Equal("Unexpected error — see the server log", problem.Detail());
        Assert.DoesNotContain("secret-looking", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Airlock", body, StringComparison.Ordinal);
        AssertFiveMembers(problem);
        var logged = Assert.Single(factory.Logs.GetSnapshot(), record => record.Id.Id == 3201);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Contains("secret-looking", logged.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_keeps_the_applications_words()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{Http.ScansUrl}/{Guid.NewGuid()}", Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found");
        Assert.Equal("Scan not found", problem.GetProperty("title").GetString());
        Assert.Equal(Refusals.ScanNotFound.Message, problem.Detail());
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", problem.GetProperty("traceId").GetString()!);
        AssertFiveMembers(problem);
    }

    internal static void AssertFiveMembers(System.Text.Json.JsonElement problem) =>
        Assert.Equal(["type", "title", "status", "detail", "traceId"], problem.EnumerateObject().Select(member => member.Name));

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Airlock.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [GeneratedRegex("^## ([a-z-]+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
