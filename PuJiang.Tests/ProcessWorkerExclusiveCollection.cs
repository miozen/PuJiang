using Xunit;

namespace PuJiang.Tests;

// Windows CI can start only a limited number of console and WPF child processes
// promptly. These tests intentionally hold files and wait at process boundaries,
// so they must not contend with one another for startup or teardown.
[CollectionDefinition("ProcessWorkerExclusive", DisableParallelization = true)]
public sealed class ProcessWorkerExclusiveCollection;
