# U4 Web API — Code Generation Summary

**Stage**: CONSTRUCTION — U4 Web API — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u4-web-api-code-generation-plan.md` (approved 2026-10-24T18:17:30Z)
**Completed**: 2026-10-24T19:30:32Z · **Branch**: `feature/u4-web-api`, from `main` fast-forwarded to the U3 commit

## Result

| Check | Target | Result |
|---|---|---|
| Build, warnings as errors (G1) | 0 warnings | 0 warnings, 0 errors |
| All tests, with Syft v1.55.0 through `SYFT_BINARY_PATH` (G2) | all pass | 916 of 919; the 3 skipped are U3's (below); three full runs in a row clean |
| Web tests | all pass | 174 of 174 |
| Domain tests | all pass | 211 of 211, unchanged |
| Application tests | all pass | 196 of 196: U3's 191 and 5 new for the amendments (B1, B3, the event hub) |
| Infrastructure tests | all pass | 270 of 273, 3 skipped: U3's 271, 3 new for the staging-root lock and 1 removed |
| Architecture tests | all pass | 65 of 65: U3's 60 and 5 new |
| Line coverage of `Airlock.Web` (G3) | 90 % | 97.9 % (896 of 915 lines) |
| Branch coverage of `Airlock.Web` (G3) | 85 % | 90.7 % (262 of 289 branches) |
| U1 to U3 still at theirs (G3) | Domain 100 %; Application 90 % and 85 %; Infrastructure 85 % and 80 % | Domain 100 % and 100 %; Application 97.1 % and 95.2 %; Infrastructure 95.5 % and 87.0 % |
| Worked examples are test rows (G4) | every example | every example (table below); two refusal rows had no test and got one |
| Rule 5 fails on a violation (G5) | — | A temporary `ArchitectureProbe` in `Airlock.Web.Api` calling `AddAirlockInfrastructure` broke the rule, which named it; the other 4 Web rules held; the probe was deleted |
| The host itself (G6) | port 8080 free; the checks below | Port 8080 free. `dotnet run` with the scratchpad's Syft: health 200 `DEGRADED` (Syft 1.55.0 passing, no NVD key); a submission 503 `not-ready` "NVD API key not configured"; a `POST` with `Sec-Fetch-Site: cross-site` 403; `Host: evil.example` 400; `/openapi/v2.json` OpenAPI 3.1.1 with the 8 paths; `/docs` in the browser pane, its network log showing requests to `localhost:8080` only |

Coverage excludes the source-generated files (the JSON context and the logging methods), as for U1 to U3. What stays
uncovered in the Web is unreachable: the default arms of the exhaustive switches over the Domain's values, and the end
of the Live Log's subscription, which ends only by cancellation.

The 3 skipped tests are U3's: the live NVD test, which runs only with `AIRLOCK_LIVE_NVD=1`; the owner-only permissions
check, which runs only on Linux and macOS; and the relative symbolic link, which needs the right to create symbolic
links on Windows.

Run them with:

```bash
dotnet build Airlock.slnx
```

```bash
dotnet test --coverage --coverage-output-format cobertura
```

The Infrastructure's integration tests need Syft through `SYFT_BINARY_PATH`, as in U3. The Web tests need no Syft, no
NVD key and no network: they replace both ports with fakes, and the tests on a real Kestrel bind a free loopback port.

## Files

### `src/Airlock.Web` — 25 files

| Folder | Types |
|---|---|
| root | `Program` (the top-level statements: create the builder, call `AirlockHost`, run); `AirlockHost`, the composition root; `ConfigurationOrder`; `Airlock.Web.csproj`, `appsettings.json`, `Properties/launchSettings.json` |
| `Api/` | `ApiEndpoints` (`AddAirlockApi`, `UseAirlockApi`, `MapAirlockApi`, `AsStateChanging`), `ScanEndpoints`, `SystemEndpoints`, `LogEndpoints`, `ApiLimits` |
| `Api/Contracts/` | The DTOs (`Dtos.cs`), the 14 contract enumerations (`ContractValues.cs`), `ApiJsonContext`, `ApiJson`, `ApiTime` |
| `Api/Mapping/` | `ScanDtoMapper`, `EventMapper` |
| `Api/Problems/` | `ApiProblems` (the 16 problem kinds, `For`, `ForRefusal`, the Web's own problems, `Customize`), `ApiExceptionHandler` |
| `Api/Uploads/` | `MultipartScanReader` and its five `SubmissionResult` records, `IdleTimeoutStream`, `RequestBodyStream` |
| `Api/Streams/` | `EventStreamResult`, `EventStreamItem` |
| `Api/Security/` | `CrossSiteRequestFilter`, `ApiResponseHeaders` |
| `Api/Docs/` | `ApiDocs` (the OpenAPI document and Scalar's options) |
| `Logging/` | `WebLog` (events 3101 to 3105, 3201, 3301) |

### `tests/Airlock.Web.Tests` — 34 code files and 1 fixture

| File | Covers |
|---|---|
| `Fixtures/worked-example-scan.json` | The worked example's finished scan, its descriptions in full from U3's recorded NVD answers (two of them linked from U3's fixtures) |
| `Support/` | `AirlockWebFactory` (the real host with fakes for Syft and NVD, its own staging root, a fake clock where needed), `FakeSbomGenerator`, `FakeVulnerabilityCatalog`, `A`, `WorkedExample`, `Http` (multipart builders and readers), `SseReader`, `TestStreams` (a generated stream, a gated upload), `Test` |
| `Contract/JsonContractTests.cs` | WR-02, WR-11: the worked example byte for byte; `null` and `[]`; UTC with milliseconds; three decimals; capitals; text as it is; compact event data |
| `Contract/MappingTests.cs` | Every warning code, unverified reason, CVSS version and score source; a cancelled scan; every Live Log level; every scan event's name |
| `Contract/ProblemDetailsTests.cs` | WR-03 to WR-05: every `RefusalCode`; 404 and 405 problems; 500 without message, stack or key; `traceId` |
| `Contract/ProblemCustomizationTests.cs` | WR-04, WR-05: problems ASP.NET Core writes itself, by status; a bad request keeps its status |
| `Contract/OpenApiDocumentTests.cs` | WR-22: every endpoint and answer, the multipart body, the problem answers, the values, the schema names; `/docs` |
| `Endpoints/ScanSubmissionTests.cs` | WR-06 to WR-09 (26 tests): the order of checks, the parts, the limits, the 202, defects 6, 7, 14 and 15 |
| `Endpoints/ScanResourceTests.cs` | WR-01, WR-10: every state; ids that are no retained scan, on all four routes; retention |
| `Endpoints/ScanEventStreamTests.cs` | WR-12, WR-13: snapshot to finished; a finished scan's two events; the framing |
| `Endpoints/ReportEndpointTests.cs` | WR-14: the PDF; none while running, failed or cancelled (defect 18) |
| `Endpoints/StopEndpointTests.cs` | WR-15, WR-16: cancel 200 or 202; Emergency Stop of a scan and of an upload (defects 3, 15) |
| `Endpoints/HealthEndpointTests.cs` | WR-17: both checks, 200 either way, no NVD call |
| `Endpoints/LiveLogStreamTests.cs` | WR-18: the replay, then live lines with their scan ids (defect 4) |
| `Security/CrossSiteTests.cs` | WR-19 to WR-21: `Sec-Fetch-Site`, `Origin`, `AllowedHosts`, no antiforgery token |
| `Host/HostCompositionTests.cs` | WR-23, U4-NFR-09, U4-NFR-10: the real composition starts `DEGRADED`; each invalid option stops it, naming it; a second Airlock on a held staging root stops, naming it, until the first stops; the configuration order; Kestrel's limit and header |
| `Quality/StreamingUploadTests.cs` | U4-NFR-02: a 256 MiB upload staged with little memory, in a collection that runs alone (`Measurements.cs`) |
| `Quality/UploadStreamTests.cs` | U4-NFR-02, U4-NFR-04: the two body wrappers |
| `Quality/KestrelTests.cs` | U4-NFR-03, U4-NFR-06: on Kestrel, the raised body limit, 413, `100-continue`, no `Server` header |
| `Quality/StalledUploadTests.cs` | U4-NFR-04: 60 seconds of fake silence end an upload and free the slot and its staging area |
| `Quality/EventStreamResultTests.cs` | U4-NFR-05, U4-NFR-09: framing, keep-alive, a closed connection, a client that stops reading, shutdown |
| `Quality/ShutdownTests.cs` | U4-NFR-05, AR-30: stopping the host ends a scan's stream with `CANCELLED` and every other stream |
| `Quality/ResponseHeaderTests.cs` | U4-NFR-06: `nosniff` and `no-store` everywhere; no CORS, no compression |
| `Quality/SecretHygieneTests.cs` | U4-NFR-06: a full scan with a key and Debug logging shows the key nowhere |
| `Quality/OfflineDocsTests.cs` | U4-NFR-07: no other host named; fonts, telemetry, agent and developer toolbar off; local scripts |
| `Quality/PerformanceSmokeTests.cs` | U4-NFR-08 at ten times the targets |

### `docs/`

`docs/api/problems.md` — the 16 problem kinds under their anchors (Q-U4-2); `docs/adr/0007-cross-site-and-host-checks.md`
(Q-U4-3).

### `tests/Airlock.Architecture.Tests`

| File | Rules |
|---|---|
| `WebModel.cs` | Loads the Web and Infrastructure assemblies; names the composition root |
| `WebRules.cs` | Rule 5's API half: no Web type but `AirlockHost` depends on an Infrastructure type, with its positive control (`AirlockHost` does); the API types are among those checked; `ScanEndpoints` reaches `StartScan`; `Program` is the only public type |

### Changed outside the new projects

- **U2** (B1, B3 and two more): `ScanReservation.RefuseBothTargets()` and `RefuseUnreadableTargetPath()`;
  `Refusals.ScanNotFound`, which `CancelScan`, `RenderScanReport` and the API use and which replaces
  `CancelScan.NotFoundMessage`;
  `ScanLifetimeService` as an `IHostedLifecycleService`; `ScanEventHub` removing a subscriber that leaves right after
  its snapshot, with an internal `SubscriberCount`. Five new tests.
- **U3** (B2): `StagingFolders.LockRoot` and the `.airlock-lock` file; `StagingSweepService` takes the lock before it
  sweeps and releases it when the host stops. Three new tests; the test that stopping the sweep did nothing is gone.
- `Directory.Packages.props` (a Web group and `Microsoft.AspNetCore.Mvc.Testing`), `Directory.Build.props` (version
  2.0.0), `Airlock.slnx` (two projects), `.gitattributes` (the fixture keeps `\n` line ends), and the
  Architecture.Tests project reference.
- `.claude/launch.json` — the desktop app's preview configuration for `dotnet run` on port 8080. It is local tooling and
  not part of the plan; whether to commit it is your call.

## Worked examples as test rows (G4)

| Worked example (`business-logic-model.md`) | Test |
|---|---|
| Submit: 202, `Location`, `id` and `RUNNING` | `ScanSubmissionTests.Artifacts_are_accepted_with_202_a_location_and_running`; `ResponseHeaderTests` for `Cache-Control` |
| Poll while it runs | `ScanResourceTests.A_running_scan_shows_its_status_phase_and_target`; `JsonContractTests.A_running_scan_has_every_property_with_null_and_empty_values` |
| The finished scan, which is also the JSON export | `JsonContractTests.The_worked_example_is_written_byte_for_byte` |
| Follow the events | `ScanEventStreamTests.A_running_scans_stream_follows_it_from_its_snapshot_to_finished`; `Each_event_is_one_event_line_and_one_data_line_without_ids` |
| A second submission while the scan runs: 409 | `A_second_submission_while_a_scan_runs_is_refused_and_the_first_is_unaffected` |
| No NVD key and no Syft: 503 with both reasons | `A_server_that_is_not_ready_refuses_with_every_reason_before_reading_the_body` |
| A mistyped path: "Target path does not exist" | `A_missing_path_is_refused_and_records_no_scan` — **new**, defect 6's regression test |
| A path outside `AllowedScanRoots` | `A_path_outside_the_allowed_scan_roots_is_refused` — **new** |
| No fields; a path and artifacts | `Neither_kind_of_target_is_refused`; `Both_kinds_of_target_are_refused_before_the_rest_of_the_body_is_read`, `Artifacts_followed_by_a_path_are_refused_too` |
| 26 artifacts; past the byte limit | `The_twenty_sixth_artifact_is_refused`; `The_byte_limit_stops_an_upload_while_it_streams` and, on Kestrel, `An_upload_past_the_scan_limit_is_answered_413_too_large` |
| `-F files=@…`, the original's field name | `An_unknown_field_is_refused_naming_it` |
| A JSON body: 415 | `A_body_that_is_not_multipart_is_unsupported_and_claims_nothing` |
| A form from another site: 403 | `CrossSiteTests` |
| Emergency Stop during an upload: 409 `upload-stopped` | `StopEndpointTests.Emergency_stop_during_an_upload_answers_that_upload_upload_stopped` |
| The report while the scan runs: 409 | `ReportEndpointTests.A_running_scans_report_is_not_ready` |
| 25 hours after the scan finished: 404 | `ScanResourceTests.A_scan_finished_more_than_a_day_ago_is_not_found` |
| Stop: `TERMINATED` and `IDLE`; cancel: 200 or 202 | `StopEndpointTests` (five tests) |
| Health: `ONLINE`, and `DEGRADED` without a key | `HealthEndpointTests.Health_reports_both_checks_and_answers_200_either_way` |
| Defects 6, 7, 14, 15 and 18 | The rows above, and the event stream and report tests |

## Deviations from the design and the plan

1. **The composition root is `AirlockHost`, not `Program`.** ArchUnitNET cannot read the method the compiler generates
   for top-level statements, so rule 5's positive control on `Program` would have checked nothing. `Program` now only
   creates the builder, calls `AirlockHost.ConfigureServices` and `ConfigurePipeline`, and runs. Rule 5 covers every
   Web type but `AirlockHost`, the API's and, from U5, the UI's. WR-23, U4-NFR-01 and the design documents say so now.
2. **The configuration move is `ConfigurationOrder.MoveLastBeforeCommandLine`** (refinement 2). `WebApplicationBuilder`
   holds two command-line sources, the host's early one and the application's last one. Placed in front of the first,
   the original variable names would have ranked below `appsettings.json`, and `LOG_LEVEL` would have lost to it. The
   source now goes in front of the last one.
3. **U2 amendment: `Refusals.ScanNotFound`.** The API answers a malformed id with the Application's own refusal,
   instead of repeating its message.
4. **U2 amendment: shutdown cancels the scan in `StoppingAsync`.** The host stops the web server before it stops the
   application's hosted services, so a scan's stream ended before its `CANCELLED` status arrived.
   `ScanLifetimeService` is now an `IHostedLifecycleService` and cancels as soon as stopping begins (AR-30 amended).
5. **U2 amendment: a subscriber that leaves right after its snapshot is removed.** `ScanEventHub` added the
   subscription before the snapshot, but removed it only from a `finally` that began after it. A client that went at
   that moment was written to until the scan ended. Found while adding the planned "no leaks" check (U4-NFR-09). The
   count lives in the Application, so the check is split: the Application tests count the subscribers, and the Web
   tests show the stream's source is disposed however the client leaves.
6. **`EventStreamResult` cancels its source's token when it ends.** A client that stopped reading — its flush reported
   completed — without the request being aborted would have left the stream waiting on its source. Found while
   covering branches.
7. **No `Accepts` metadata on the submission.** With it, routing answered 415 itself, before the cross-site filter and
   without log event 3102. The OpenAPI document gets the multipart body from an operation transformer, and the
   `ScanSubmission` type is gone. The streams and the PDF declare `Produces<Stream>` with their content types, without
   which the document left their content out.
8. **The reader compares the part's disposition with `form-data` itself.** ASP.NET Core's `IsFormDisposition()` is
   false for any part carrying a file name, so every artifact would have been refused.
9. **Scalar's developer toolbar is hidden.** Scalar shows it on localhost, and its Share and Deploy buttons send the
   document to Scalar's service (U4-NFR-07). Found in G6. Scalar also answers `/docs` with a redirect to `/docs/`.
10. **Test infrastructure.**
    - The `WebApplicationFactory` client follows no redirects and keeps no cookies, because its redirect handler
      buffers every request body, which the streaming tests cannot live with.
    - The tests that need a failed start build `AirlockHost` themselves, on a loopback port of their own. Through the
      factory, the application's own thread disposes the host when startup fails, and the factory then sometimes
      reported the disposed service provider instead of the reason. Found as a flaky test: 2 failures in 5 full runs.
11. **Names.** The plan's
    - `LogEntryDtoMapper` is `EventMapper`, which maps every scan event and Live Log line to its server-sent event;
    - `OpenApiTransformers` and `ScalarSetup` are `ApiDocs`;
    - `CustomizeProblemDetails` is `ApiProblems.Customize`;
    - `BodyLimitTests` is `KestrelTests`;
    - `KeepAliveAndShutdownTests` is `EventStreamResultTests` and `ShutdownTests`;
    - `LeakTests` became the checks in deviation 5 and the slot and staging assertions of the misbehaving-client tests.

    `MultipartScanReader` checks the media type first (`CheckContentType`), then answers a `SubmissionResult` for the
    endpoint to turn into its answer. `component-methods.md` and `logical-components.md` show the shapes.
12. **An unknown field's name is cut by U2's `ExternalText`** to 64 characters, the last an ellipsis.

## Constraints handed to U5 and U6

The constraints in `nfr-design/logical-components.md` and `infrastructure-design/deployment-architecture.md` stand.
Code Generation adds these:

- **U5 (UI)**:
  - Nothing answers `/` yet, and `launchSettings.json` opens `/docs`. U5 maps the UI and points the launch URL at it
    (refinement 5).
  - Rule 5 already covers the UI's types: components call Application use cases, never Infrastructure types, and only
    `AirlockHost` registers anything.
  - A component that follows a scan through `WatchScan` disposes its subscription when it is disposed; the hub then
    stops writing to it at once.
  - The status-code pages and the exception handler run only under `/api`; the UI brings its own.
- **U6 (delivery)**:
  - For a local run with Syft outside `bin/`, set `SYFT_BINARY_PATH` or pass `--Airlock:Syft:BinaryPath=<path>`.
  - The Web tests need no Syft, no key and no network, only loopback ports; CI runs them on Windows and Linux.
