// Every test class hosts its own gateway; hosting several Program entry points at once makes
// WebApplicationFactory pick up the wrong host, so classes run one after another.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
