// 2026-08-06: xUnit runs tests in parallel by default
// (different test classes → different threads). Process
// env vars are global. Test classes that mutate
// AICHAT_API_KEY / AICHAT_PROVIDER_xxx_API_KEY /
// AICHAT_ISOLATED_DATA_ROOT must run serially with any
// test class that observes settings.json's apiKey field,
// otherwise the observed value depends on whichever env-
// mutating test ran most recently in another thread.
//
// Marking the relevant test classes with
// [Collection(ProcessEnvMutatingCollection.Name)] forces
// xUnit to run them in a single thread. The collection has
// no fixture logic — the env-var reset is the test class's
// own Dispose — but the serialisation itself is what makes
// the storage tests stable.
//
// JsonAppRepositoryTests is included because it reads
// settings.json's apiKey field, which EnvironmentSecretOverride
// would silently override (the legacy "I should see the value
// from disk" assertion failed ~80% of the time on a 5-run
// matrix before this collection existed).
[CollectionDefinition(Name)]
public sealed class ProcessEnvMutatingCollection
{
    public const string Name = "ProcessEnvMutating";
}
