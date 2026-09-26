using Xunit;

// This assembly's tests mutate process-wide static state (CocoNut.Localization.Strings.Culture, set by
// LocalizationBootstrapTests and read by every other test that asserts on a localized string) alongside
// Avalonia's single headless UI session. Disabling parallelization keeps both kinds of test deterministic;
// the suite is small enough that running it sequentially costs nothing noticeable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
