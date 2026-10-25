namespace Airlock.Web.Tests.Quality;

/// <summary>Tests that measure the whole process — allocations, timings — and so run alone.</summary>
[CollectionDefinition(nameof(Measurements), DisableParallelization = true)]
public sealed class Measurements;
