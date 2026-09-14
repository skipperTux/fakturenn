// This assembly runs one test at a time, deliberately -- the same reason
// Fakturenn.UiTests does, arrived at the same way.
//
// Four classes here build a real host through FakturennWebApplication.Build --
// SharedResourceTests, AccountFormsTests, IdentityConfigurationTests and
// MessagingCompositionTests, which builds one per guard -- nine build sites in all.
// Build assigns a fresh Serilog bootstrap
// ReloadableLogger to the process-global Log.Logger, and the host's AddSerilog freezes it
// when the service provider resolves the logger. Two classes doing that at once race on
// one process-wide object.
//
// MEASURED, not assumed. CI failed with collections running in parallel:
//
//   System.InvalidOperationException : The logger is already frozen.
//     at Serilog.Extensions.Hosting.ReloadableLogger.Freeze()
//     at Serilog.SerilogServiceCollectionExtensions.<>c__DisplayClass3_0.<AddSerilog>b__0
//     at ...MessagingCompositionTests.The_invoices_context_is_enrolled_with_the_outbox()
//
// It went green five times out of five locally and failed on a two-core runner, which is
// the frequency that gets a failure dismissed as unrelated to the change that exposed it.
// It surfaced during a dependency bump and had nothing to do with the bump: the Serilog
// versions resolve identically on main and on the bump branch.
//
// This is a test-harness constraint, NOT a product defect -- a deployed instance is one
// host in one process. Do not "fix" it by retrying, by widening a timeout, or by having
// each test save and restore Log.Logger: the integration suite needs that save/restore
// because it builds a second host beside a long-lived fixture host, whereas here nothing
// has to overlap in the first place.
//
// The cost is a few seconds. Every test in this assembly is in-process with no database.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
