using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Airlock.Application.Ports;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Endpoints;

// WR-06 to WR-09 — the order of checks, the parts, the limits and the 202, defect 14's asynchronous submission.
public sealed class ScanSubmissionTests
{
    [Fact]
    public async Task Artifacts_are_accepted_with_202_a_location_and_running()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Requirements(), Test.Token);
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = body.GetProperty("id").GetString()!;
        Assert.Equal($"/api/v2/scans/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("RUNNING", body.GetProperty("status").GetString());
        Assert.Equal(["id", "status"], body.EnumerateObject().Select(member => member.Name));
        Assert.Equal("COMPLETED", (await client.WaitForEndAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_directory_is_accepted()
    {
        await using var factory = new AirlockWebFactory();
        using var directory = new TemporaryFolder();
        using var client = factory.CreateClient();

        var id = await client.StartScanAsync(Http.Directory(directory.Path));

        var scan = await client.WaitForEndAsync(id);
        Assert.Equal("DIRECTORY", scan.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(directory.Path, scan.GetProperty("target").GetProperty("path").GetString());
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_is_unsupported_and_claims_nothing()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var json = new StringContent("{\"targetPath\":\"/uploads\"}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Http.ScansUrl, json, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported-media-type");
        Assert.Equal("Scans are submitted as multipart/form-data", problem.Detail());
        await client.StartScanAsync(Http.Requirements());
    }

    [Fact]
    public async Task A_multipart_type_without_a_boundary_is_invalid()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(A.Bytes("--x--"));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

        using var response = await client.PostAsync(Http.ScansUrl, content, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request");
        Assert.Equal("The multipart boundary is missing or too long", problem.Detail());
    }

    [Fact]
    public async Task A_server_that_is_not_ready_refuses_with_every_reason_before_reading_the_body()
    {
        await using var factory = new AirlockWebFactory();
        factory.Catalog.IsConfigured = false;
        factory.Sbom.Status = new SbomToolStatus(false, null, "Syft not found at /opt/airlock/bin/syft");
        using var client = factory.CreateClient();
        var upload = new GeneratedStream(64L * 1024 * 1024);
        using var form = new MultipartFormDataContent { { new StreamContent(upload), "artifacts", "app-image.tar" } };

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.ServiceUnavailable, "not-ready");
        Assert.Equal("NVD API key not configured; Syft not found at /opt/airlock/bin/syft", problem.Detail());
        Assert.True(upload.BytesRead < 8 * 1024 * 1024, $"{upload.BytesRead} bytes were read.");
        Assert.Empty(factory.StagingFolders());
    }

    [Fact]
    public async Task A_second_submission_while_a_scan_runs_is_refused_and_the_first_is_unaffected()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var client = factory.CreateClient();
        var first = await client.StartScanAsync(Http.Requirements());

        using var second = await client.PostAsync(Http.ScansUrl, Http.Requirements(), Test.Token);

        var problem = await second.ProblemAsync(HttpStatusCode.Conflict, "scan-in-progress");
        Assert.Equal("A scan is already in progress", problem.Detail());
        Assert.Equal("RUNNING", (await client.GetScanAsync(first)).GetProperty("status").GetString());
        using var stop = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        await client.WaitForEndAsync(first);
    }

    [Fact]
    public async Task A_submission_while_another_uploads_is_refused()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = client.PostAsync(Http.ScansUrl, GatedUpload.Create("app-image.tar", gate.Task), Test.Token);
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 1, "The first upload never reached staging.");

        using var second = await client.PostAsync(Http.ScansUrl, Http.Requirements(), Test.Token);

        await second.ProblemAsync(HttpStatusCode.Conflict, "scan-in-progress");
        gate.SetResult();
        using var first = await uploading.Within();
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
    }

    [Fact]
    public async Task An_unknown_field_is_refused_naming_it()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(A.Bytes(A.RequirementsText)), "files", "requirements.txt" } };

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request");
        Assert.Equal("Unknown form field 'files'; expected 'targetPath' or 'artifacts'", problem.Detail());
        Assert.Contains(factory.Logs.GetSnapshot(), record => record.Id.Id == 3103);
    }

    [Fact]
    public async Task An_unknown_fields_name_is_shown_cleaned_and_cut()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var name = "evil" + new string('x', 100);

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(Http.Part($"name=\"{name}\"", "1") + Http.End()), Test.Token);

        var detail = (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request")).Detail();
        Assert.Contains("'evil" + new string('x', 59) + "…'", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_target_path_is_refused()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var body = Http.Part("name=\"targetPath\"", "/uploads/a") + Http.Part("name=\"targetPath\"", "/uploads/b") + Http.End();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(body), Test.Token);

        Assert.Equal("targetPath may be given only once", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request")).Detail());
    }

    [Fact]
    public async Task An_artifact_without_a_file_name_is_refused()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent { { new StringContent("flask==0.12.2"), "artifacts" } };

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        Assert.Equal("Each artifact needs a file name", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request")).Detail());
    }

    [Fact]
    public async Task A_browsers_empty_file_input_is_skipped()
    {
        await using var factory = new AirlockWebFactory();
        using var directory = new TemporaryFolder();
        using var client = factory.CreateClient();
        var body = Http.Part("name=\"targetPath\"", directory.Path) + Http.Part("name=\"artifacts\"; filename=\"\"", string.Empty) + Http.End();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(body), Test.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var scan = await client.WaitForEndAsync((await response.JsonAsync()).GetProperty("id").GetString()!);
        Assert.Equal("DIRECTORY", scan.GetProperty("target").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Both_kinds_of_target_are_refused_before_the_rest_of_the_body_is_read()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var upload = new GeneratedStream(64L * 1024 * 1024);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("/uploads"), "targetPath" },
            { new StreamContent(upload), "artifacts", "app-image.tar" },
        };

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target");
        Assert.Equal("Choose a directory or upload artifacts, not both", problem.Detail());
        Assert.True(upload.BytesRead < 8 * 1024 * 1024, $"{upload.BytesRead} bytes were read.");
        Assert.Empty(factory.StagingFolders());
    }

    [Fact]
    public async Task Artifacts_followed_by_a_path_are_refused_too()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var form = Http.Requirements();
        form.Add(new StringContent("/uploads"), "targetPath");

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        Assert.Equal("Choose a directory or upload artifacts, not both", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
    }

    [Fact]
    public async Task Neither_kind_of_target_is_refused()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(Http.End()), Test.Token);

        Assert.Equal("Choose a directory or upload at least one artifact", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
    }

    // Defect 6: the original read a mistyped path as "0 packages, all clean".
    [Fact]
    public async Task A_missing_path_is_refused_and_records_no_scan()
    {
        await using var factory = new AirlockWebFactory();
        using var directory = new TemporaryFolder();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Directory(Path.Combine(directory.Path, "acme-ap")), Test.Token);

        Assert.Equal("Target path does not exist", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
        Assert.Null(response.Headers.Location);
        Assert.Empty(factory.Sbom.Calls);
        Assert.Empty(factory.StagingFolders());
        await client.WaitForEndAsync(await client.StartScanAsync(Http.Directory(directory.Path)));
    }

    [Fact]
    public async Task A_path_outside_the_allowed_scan_roots_is_refused()
    {
        using var directory = new TemporaryFolder();
        var uploads = Directory.CreateDirectory(Path.Combine(directory.Path, "uploads")).FullName;
        var elsewhere = Directory.CreateDirectory(Path.Combine(directory.Path, "etc")).FullName;
        await using var factory = new AirlockWebFactory(settings => settings["Airlock:AllowedScanRoots:0"] = uploads);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Directory(elsewhere), Test.Token);

        Assert.Equal("Path is outside the allowed scan roots", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
        Assert.Empty(factory.Sbom.Calls);
        await client.WaitForEndAsync(await client.StartScanAsync(Http.Directory(uploads)));
    }

    [Fact]
    public async Task A_broken_body_is_invalid()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var truncated = "--airlock-test\r\nContent-Disposition: form-data; name=\"artifacts\"; filename=\"requirements.txt\"\r\n\r\nflask==0.12.2";

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(truncated), Test.Token);

        Assert.Equal("The request body is not valid multipart/form-data", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request")).Detail());
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
    }

    [Theory]
    [InlineData("Content-Disposition: attachment; name=\"targetPath\"")]
    [InlineData("Content-Type: text/plain")]
    public async Task A_part_that_is_not_form_data_is_invalid(string header)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var body = $"--airlock-test\r\n{header}\r\n\r\n/uploads\r\n" + Http.End();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(body), Test.Token);

        Assert.Equal("The request body is not valid multipart/form-data", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-request")).Detail());
    }

    [Fact]
    public async Task A_disconnect_frees_the_slot_and_deletes_the_staging_area()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(Test.Token);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = client.PostAsync(Http.ScansUrl, GatedUpload.Create("app-image.tar", gate.Task), abandon.Token);
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 1, "The upload never reached staging.");

        await abandon.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploading.Within());
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
        await client.StartScanWhenFreeAsync(Http.Requirements);
        Assert.Contains(factory.Logs.GetSnapshot(), record => record.Id.Id == 3105);
    }

    [Fact]
    public async Task The_twenty_sixth_artifact_is_refused()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var form = Http.Artifacts([.. Enumerable.Range(0, 26).Select(i => ($"file-{i}.bin", new byte[] { 1 }))]);

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        Assert.Equal("At most 25 files per scan", (await response.ProblemAsync(HttpStatusCode.BadRequest, "too-many-artifacts")).Detail());
    }

    [Fact]
    public async Task The_byte_limit_stops_an_upload_while_it_streams()
    {
        await using var factory = new AirlockWebFactory(settings => settings["Airlock:Limits:MaxBytesPerScan"] = (1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var client = factory.CreateClient();
        var upload = new GeneratedStream(16L * 1024 * 1024);
        using var form = new MultipartFormDataContent { { new StreamContent(upload), "artifacts", "big.bin" } };

        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);

        Assert.Equal("Uploads are limited to 1 MB per scan", (await response.ProblemAsync(HttpStatusCode.RequestEntityTooLarge, "too-large")).Detail());
        Assert.True(upload.BytesRead < 8 * 1024 * 1024, $"{upload.BytesRead} bytes were read.");
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
    }

    [Fact]
    public async Task A_hostile_file_name_is_stored_under_its_sanitized_name()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        var id = await client.StartScanAsync(Http.Artifacts(("../../evil.txt", [1, 2, 3])));

        var artifact = (await client.WaitForEndAsync(id)).GetProperty("target").GetProperty("artifacts")[0];
        Assert.Equal("evil.txt", artifact.GetProperty("fileName").GetString());
        Assert.Equal(3, artifact.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("OTHER", artifact.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_target_path_that_is_not_utf8_is_not_valid()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        byte[] body = [.. A.Bytes("--airlock-test\r\nContent-Disposition: form-data; name=\"targetPath\"\r\n\r\n/uploads/"), 0xFF, 0xFE, .. A.Bytes("\r\n" + Http.End())];

        using var response = await client.PostAsync(Http.ScansUrl, Http.Raw(body), Test.Token);

        Assert.Equal("Target path is not valid", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
    }

    [Fact]
    public async Task A_target_path_past_sixteen_kibibytes_is_not_valid()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Http.ScansUrl, Http.Directory("/" + new string('a', 17_000)), Test.Token);

        Assert.Equal("Target path is not valid", (await response.ProblemAsync(HttpStatusCode.BadRequest, "invalid-target")).Detail());
    }

    [Fact]
    public async Task Field_names_ignore_case_and_artifacts_may_carry_brackets()
    {
        await using var factory = new AirlockWebFactory();
        using var directory = new TemporaryFolder();
        using var client = factory.CreateClient();

        var byPath = await client.StartScanAsync(Http.Raw(Http.Part("name=\"TargetPath\"", directory.Path) + Http.End()));
        await client.WaitForEndAsync(byPath);
        var byBrackets = await client.StartScanWhenFreeAsync(() => Http.Raw(Http.Part("name=\"artifacts[]\"; filename=\"requirements.txt\"", A.RequirementsText) + Http.End()));

        var scan = await client.WaitForEndAsync(byBrackets);
        Assert.Equal("requirements.txt", scan.GetProperty("target").GetProperty("artifacts")[0].GetProperty("fileName").GetString());
    }
}
