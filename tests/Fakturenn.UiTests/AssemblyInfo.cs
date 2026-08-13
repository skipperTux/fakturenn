// This assembly runs one test at a time, deliberately.
//
// It hosts THREE real applications in one process -- WebAppFixture for the anonymous
// pages, and two AuthenticatedWebAppFixture instances, one for the shared identity
// collection and one for the Content-Security-Policy walk, which needs a /setup page that
// no user has closed yet. Every one of them builds EF Core models for the same three
// context types.
//
// MEASURED, not assumed. With collections running in parallel, two fixtures initialising
// at once intermittently killed the whole shared collection:
//
//   System.InvalidOperationException: The model must be finalized and its runtime
//   dependencies must be initialized before 'GetRelationalModel' can be used.
//     at Microsoft.EntityFrameworkCore.RelationalModelExtensions.GetRelationalModel(IModel)
//     at Microsoft.EntityFrameworkCore.Migrations.Internal.Migrator.HasPendingModelChanges()
//     at Fakturenn.UiTests.AuthenticatedWebAppFixture.MigrateAsync()
//
// -- one thread reading a model another thread was still finalising, out of EF's process-
// wide internal service provider. It reproduced twice in eleven runs and left nine runs
// perfectly green in between, which is the worst possible frequency: often enough to fail
// a pipeline, rare enough to be dismissed as "flaky Playwright".
//
// This is a test-harness constraint, NOT a product defect: a deployed instance is one host
// in one process, and Task 14's UserTokenProtectorModelCacheKeyFactory already covers the
// one case where two providers legitimately meet. Do not "fix" it by widening a timeout or
// by retrying the fixture.
//
// The cost is small. The shared collection is dominated by the sixty-second security-stamp
// wait in Locking_a_user_stops_their_existing_session, and the other collections are short.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
