# Wolverine durable processing — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Fakturenn a working PostgreSQL-backed transactional outbox that nothing publishes through yet, closing M0.

**Architecture:** Wolverine runs in-process inside `Fakturenn.Web`. A new `Fakturenn.Infrastructure.Messaging` assembly owns registration and storage provisioning; Wolverine owns its own table definitions in a `messaging` schema, and `--migrate` owns when they are created. `InvoicesDbContext` is enrolled with the EF Core outbox so a slice's rows and its queued messages commit together.

**Tech Stack:** .NET 10, Wolverine (`WolverineFx.Postgresql`, `WolverineFx.EntityFrameworkCore`), EF Core 10, PostgreSQL, xUnit v3 with Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-19-wolverine-durable-processing-design.md`

## Global Constraints

- **Central Package Management.** Never write `Version=` on a `PackageReference`. Use `dotnet add package`, which strips the trailing newline from `Directory.Packages.props` — restore it.
- **Warnings are errors.** `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`. `CA1848` and `CA1873` mean logging goes through `[LoggerMessage]` partial methods; an `IsEnabled` guard does not satisfy CA1873.
- **Migrations never run at startup.** Only `--migrate` touches the schema.
- **`DOTNET_USE_POLLING_FILE_WATCHER=1`** for every test run on this workstation.
- **Every bash command starts** `...`.
- **Mutation hygiene.** Never revert with `git checkout --` on a file whose content is uncommitted; back up or regenerate. After every mutation run `git status --short src/` and confirm it is empty.
- **Baseline before this plan:** unit 31, Identity unit 31, Web unit 67, architecture 14, compliance 10, integration 112, UI 15. Build 0/0, format clean.

---

### Task 1: The messaging assembly and its provisioning seam

Creates the assembly, registers Wolverine with PostgreSQL persistence in a `messaging` schema, and exposes one explicit provisioning call. Nothing is wired into the host yet, so `--migrate` and the running application are unchanged at the end of this task.

**Files:**
- Create: `src/Fakturenn.Infrastructure.Messaging/Fakturenn.Infrastructure.Messaging.csproj`
- Create: `src/Fakturenn.Infrastructure.Messaging/MessagingConfiguration.cs`
- Create: `src/Fakturenn.Infrastructure.Messaging/MessagingStorage.cs`
- Modify: `Fakturenn.slnx`
- Modify: `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`
- Modify: `tests/Fakturenn.ArchitectureTests/Fakturenn.ArchitectureTests.csproj`
- Modify: `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `MessagingConfiguration.SchemaName` — `public const string` = `"messaging"`
  - `MessagingConfiguration.AddFakturennMessaging(this IHostApplicationBuilder builder, string? connectionString)` — returns `void`
  - `MessagingStorage.ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken)` — returns `Task`

- [ ] **Step 1: Create the project and add packages**

```bash
cd /home/christoph/Projects/fakturenn
dotnet new classlib --output src/Fakturenn.Infrastructure.Messaging --name Fakturenn.Infrastructure.Messaging
rm --force src/Fakturenn.Infrastructure.Messaging/Class1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Infrastructure.Messaging/Fakturenn.Infrastructure.Messaging.csproj
dotnet add src/Fakturenn.Infrastructure.Messaging package WolverineFx.Postgresql
dotnet add src/Fakturenn.Infrastructure.Messaging package WolverineFx.EntityFrameworkCore
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Infrastructure.Messaging
```

`dotnet new classlib` does not produce a csproj matching this repo. Mirror `src/Fakturenn.Infrastructure.DataProtection/Fakturenn.Infrastructure.DataProtection.csproj` for property ordering and add `net10.0`, `ImplicitUsings`, `Nullable`. Restore the trailing newline in `Directory.Packages.props`.

- [ ] **Step 2: Write the registration**

`src/Fakturenn.Infrastructure.Messaging/MessagingConfiguration.cs`:

```csharp
using JasperFx.Resources;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Fakturenn.Infrastructure.Messaging;

/// <summary>
/// Registers Wolverine with PostgreSQL-backed durable local queues.
/// <para>
/// Infrastructure rather than a module: no module owns "messaging", and
/// MODULE-OWNERSHIP.md's OutboundMessage belongs to Mail, which is application-level
/// delivery tracking rather than this transport storage. Modules never reference this
/// assembly; architecture rule 4 enforces that by name pattern.
/// </para>
/// </summary>
public static class MessagingConfiguration
{
    /// <summary>
    /// Wolverine's own envelope, dead-letter and node tables live here. Fourth schema
    /// alongside identity, invoices and dataprotection.
    /// </summary>
    public const string SchemaName = "messaging";

    public static void AddFakturennMessaging(this IHostApplicationBuilder builder, string? connectionString)
    {
        builder.UseWolverine(options =>
        {
            options.PersistMessagesWithPostgresql(connectionString ?? string.Empty, SchemaName);

            // Durable, not in-memory. This is the choice that makes the storage real --
            // an in-memory transport would pass every delivery test and lose everything
            // on restart, which is why a test asserts an envelope reaches the table.
            options.Policies.UseDurableLocalQueues();

            // Deliberately NOT calling UseResourceSetupOnStartup(). Wolverine creates its
            // schema on boot when that is present, and CLAUDE.md's invariant is that
            // migrations never run automatically at startup -- only --migrate touches the
            // schema. MessagingStorage.ProvisionAsync is how the tables get created.
        });
    }
}
```

Confirm `UseWolverine` is available on `IHostApplicationBuilder` in the restored package version; if it is only on `IHostBuilder`, take `WebApplicationBuilder` and call `builder.Host.UseWolverine(...)` instead, and say so in the report.

- [ ] **Step 3: Write the provisioning seam**

`src/Fakturenn.Infrastructure.Messaging/MessagingStorage.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Persistence.Durability;

namespace Fakturenn.Infrastructure.Messaging;

/// <summary>
/// Creates Wolverine's tables, on demand and never at startup.
/// <para>
/// Wolverine owns these table definitions and changes them between versions. Writing EF
/// migrations that mirror a library's private schema would mean re-deriving them on every
/// upgrade, and getting that wrong fails at runtime inside message storage rather than at
/// build time. So the library owns the DDL and this project owns the timing.
/// </para>
/// </summary>
public static class MessagingStorage
{
    public static async Task ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IMessageStore store = services.GetRequiredService<IMessageStore>();
        await store.Admin.MigrateAsync(cancellationToken);
    }
}
```

If `MigrateAsync` does not accept a `CancellationToken` in the restored version, call the parameterless overload and keep the parameter on `ProvisionAsync` — callers pass one and the signature should not churn when the library adds it.

- [ ] **Step 4: Add the assembly to the architecture loader and the anti-vacuity list**

In `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`, add to the `LoadAssemblies(...)` call:

```csharp
            typeof(Infrastructure.Messaging.MessagingConfiguration).Assembly,
```

In `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`, add to the hardcoded list in `The_architecture_contains_the_assemblies_the_rules_govern`:

```csharp
            "Fakturenn.Infrastructure.Messaging",
```

That list asserts with `.Should().Contain(...)`, a subset check, so omitting the entry fails nothing. It is added so the guard names every assembly it claims to guard.

- [ ] **Step 5: Prove the loader line is load-bearing**

Remove the `typeof(...)` line added in Step 4, run the architecture suite, and confirm **two** tests fail: `The_loader_omits_no_assembly_declared_under_src_in_the_solution` and `The_architecture_contains_the_assemblies_the_rules_govern`. Restore the line.

Run: `cd /home/christoph/Projects/fakturenn && DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.ArchitectureTests --configuration Release`
Expected without the line: 2 failed. With it: 14 passed.

If only one fails, the Step 4 anti-vacuity entry is missing.

- [ ] **Step 6: Build and verify nothing else moved**

Run: `cd /home/christoph/Projects/fakturenn && dotnet build --configuration Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run every suite. Expected unchanged from the baseline except architecture, still 14.

- [ ] **Step 7: Commit**

```bash
cd /home/christoph/Projects/fakturenn
git add src/Fakturenn.Infrastructure.Messaging tests/Fakturenn.ArchitectureTests Fakturenn.slnx Directory.Packages.props
git commit --message "feat(messaging): add the Wolverine assembly and its provisioning seam

Wolverine owns its table definitions and this project owns the timing.
UseResourceSetupOnStartup is deliberately absent: it creates the schema on boot,
and only --migrate may touch the schema here.

Nothing is wired into the host yet, so --migrate and the running application are
unchanged by this commit.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Wire the host and the `--migrate` entrypoint

Registers messaging in the host and makes `--migrate` provision it. At the end, a real database gains a `messaging` schema when an operator runs `--migrate`, and never otherwise.

**Files:**
- Modify: `src/Fakturenn.Web/Fakturenn.Web.csproj`
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs`
- Modify: `src/Fakturenn.Web/Program.cs`
- Modify: `src/Fakturenn.Web/MigrationSeedLog.cs`
- Test: `tests/Fakturenn.IntegrationTests/MigrateEntrypointTests.cs`

**Interfaces:**
- Consumes: `MessagingConfiguration.AddFakturennMessaging`, `MessagingConfiguration.SchemaName`, `MessagingStorage.ProvisionAsync` from Task 1
- Produces: a `--migrate` run that creates the `messaging` schema; `MigrationSeedLog.ProvisionedMessagingStorage(ILogger)` and `MigrationSeedLog.MessagingProvisioningFailed(ILogger, string)`

- [ ] **Step 1: Reference the assembly and register it**

```bash
cd /home/christoph/Projects/fakturenn
dotnet add src/Fakturenn.Web reference src/Fakturenn.Infrastructure.Messaging
```

In `src/Fakturenn.Web/FakturennWebApplication.cs`, beside the existing `builder.AddFakturennIdentity(connectionString, databaseOptions);` call:

```csharp
        builder.AddFakturennMessaging(connectionString);
```

Add `using Fakturenn.Infrastructure.Messaging;`.

- [ ] **Step 2: Write the failing subprocess test**

`--migrate` lives in `Program.cs` top-level statements, which no in-process test can reach; a test over `MessagingStorage` alone would pass while the dispatch was missing. `MigrateEntrypointTests` already runs the built assembly as a subprocess — follow it.

Add to `tests/Fakturenn.IntegrationTests/MigrateEntrypointTests.cs`:

```csharp
    [Fact]
    public async Task Migrate_provisions_the_messaging_schema()
    {
        // Wolverine's tables are created here and nowhere else: the host omits
        // UseResourceSetupOnStartup *and* sets AutoBuildMessageStorageOnStartup to
        // AutoCreate.None, so an application that has never been migrated has no
        // message storage at all. Both knobs are needed -- see the design's section 4.
        //
        // Sketch only: PostgresFixture.StartAsync() does not exist. The committed test
        // takes the database from the class fixture instead.

        int exitCode = await RunMigrateAsync(database.ConnectionString);
        exitCode.Should().Be(0);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'messaging'";
        object? found = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Convert.ToInt64(found, CultureInfo.InvariantCulture).Should().Be(
            1,
            "--migrate is the only thing that may create Wolverine's schema");
    }
```

Reuse whatever fixture and `RunMigrateAsync` helper that file already defines rather than inventing new ones; match its existing idiom exactly.

- [ ] **Step 3: Run it and watch it fail**

Run: `cd /home/christoph/Projects/fakturenn && DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.IntegrationTests --configuration Release --filter-query '/*/*/MigrateEntrypointTests/Migrate_provisions_the_messaging_schema'`
Expected: FAIL — the `messaging` schema does not exist, count is 0.

- [ ] **Step 4: Add the provisioning step to `--migrate`**

In `src/Fakturenn.Web/Program.cs`, after `DatabaseMigrator.RunAsync` returns and before the seeding block, inside `if (exitCode == 0)`:

```csharp
    // Step two of the migrate operation. EF migrations first, then messaging storage:
    // Wolverine needs none of the business schemas, but a run that created messaging
    // tables against a database with no business schema is a confusing halfway state.
    //
    // These are steps of ONE operation, not a menu. A future migration service exposes a
    // single "migrate" that succeeds or fails; a caller never chooses which steps run.
    //
    // There is no rollback across these steps. PostgreSQL makes DDL transactional per
    // migration, but nothing wraps EF migrations plus provisioning plus seeding, so a
    // failure here leaves a partially migrated database. The obligation is to name the
    // step, exit non-zero, and leave the operator to restore the backup they took before
    // starting -- never to half-repair.
    if (exitCode == 0)
    {
        try
        {
            await MessagingStorage.ProvisionAsync(app.Services, CancellationToken.None);
            MigrationSeedLog.ProvisionedMessagingStorage(migrationLogger);
        }
        catch (Exception failure)
        {
            MigrationSeedLog.MessagingProvisioningFailed(migrationLogger, failure.Message);
            exitCode = 1;
        }
    }
```

Add `using Fakturenn.Infrastructure.Messaging;`.

In `src/Fakturenn.Web/MigrationSeedLog.cs`, add:

```csharp
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Provisioned messaging storage.")]
    public static partial void ProvisionedMessagingStorage(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Could not provision messaging storage: {Reason}. The database may be "
            + "partially migrated. Restore the backup taken before this run rather than "
            + "re-running against this state.")]
    public static partial void MessagingProvisioningFailed(ILogger logger, string reason);
```

Catching bare `Exception` is deliberate here and only here: the operation's contract is that any failure is reported precisely and exits non-zero, and Wolverine's provisioning surfaces provider-specific exception types this project does not model.

- [ ] **Step 5: Run the test and watch it pass**

Run the same filtered command as Step 3.
Expected: PASS.

- [ ] **Step 6: Prove the dispatch is what the test covers**

Delete the `MessagingStorage.ProvisionAsync` call added in Step 4, rebuild, and re-run the integration suite. Confirm `Migrate_provisions_the_messaging_schema` reddens and nothing else does. Restore it, and confirm `git status --short src/` is empty afterwards.

- [ ] **Step 7: Superseded -- the startup invariant is now covered by a test**

This step originally started the application against an unmigrated database and
expected `/alive` 200 with no `messaging` schema. **That expectation is wrong and the
step cannot pass.** The design's section 4, amended after this plan was written, rules
that a host configured with a connection string against an unprovisioned database
*crashes during `StartAsync`*, deliberately: Wolverine's durability agent queries
`messaging.wolverine_nodes` unconditionally, and a database that has never been
migrated is a deployment error rather than a transient fault.

The invariant itself still holds and is now enforced automatically, which is better
than a manual step nobody re-runs:
`MessagingStartupTests.Startup_does_not_provision_the_messaging_schema` boots the real
host against an EF-migrated database with no `messaging` schema and asserts both that
startup fails and that the schema is still absent. Its companion,
`MessagingStartupTests.A_host_with_no_connection_string_reports_that_messages_are_not_durable`,
covers the other un-provisioned case: no connection string configured, where the host
*does* start on in-memory queues and must say so at Critical.

- [ ] **Step 8: Run everything and commit**

Run every suite, `dotnet build --configuration Release`, `dotnet format --verify-no-changes`.

```bash
cd /home/christoph/Projects/fakturenn
git add src/Fakturenn.Web tests/Fakturenn.IntegrationTests
git commit --message "feat(messaging): provision message storage from --migrate

EF migrations first, then messaging storage, then seeding and catalogue
validation -- steps of one operation, not a menu a caller picks from.

There is no rollback across those steps, so a provisioning failure names the
step, exits non-zero and tells the operator to restore, rather than implying a
repair it cannot perform.

Verified by hand that an unmigrated database gains no messaging schema when the
application starts: Wolverine's UseResourceSetupOnStartup is deliberately absent.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Enrol the outbox and prove it is transactional

Enrols `InvoicesDbContext` with the EF Core outbox and proves the three properties that are ours: a rollback delivers nothing, a commit delivers, and the envelope is persisted rather than held in memory.

**Files:**
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs:95`
- Modify: `src/Fakturenn.Infrastructure.Messaging/MessagingConfiguration.cs` — adds `UseEntityFrameworkCoreTransactions()`
- Create: `tests/Fakturenn.IntegrationTests/Messaging/OutboxProbe.cs`
- Create: `tests/Fakturenn.IntegrationTests/Messaging/OutboxTransactionTests.cs`

**Interfaces:**
- Consumes: `MessagingConfiguration.AddFakturennMessaging` from Task 1, provisioning from Task 2
- Produces:
  - `OutboxProbe.Message` — `public sealed record OutboxProbeMessage(Guid Id)`
  - `OutboxProbe.Handled` — `public static ConcurrentBag<Guid>` recording handled ids

- [ ] **Step 1: Enrol the context**

`src/Fakturenn.Web/FakturennWebApplication.cs` currently registers the context at line 95:

```csharp
        builder.Services.AddDbContext<InvoicesDbContext>(options =>
            options.UseNpgsql(connectionString));
```

Replace with Wolverine's integrated registration:

```csharp
        // Enrolled with the outbox so a slice's rows and its queued messages commit in one
        // transaction. The enrolment lives here, in the host: Fakturenn.Modules.Invoices
        // references neither Wolverine nor Fakturenn.Infrastructure.Messaging, and its
        // DbContext is untouched -- the same arrangement as the audit interceptor.
        //
        // InvoicesDbContext is schema-only today (no DbSet, one migration that creates the
        // schema). Enrolling an empty context is fine: the outbox binds to its connection
        // and transaction, not to its entities.
        builder.Services.AddDbContextWithWolverineIntegration<InvoicesDbContext>(options =>
            options.UseNpgsql(connectionString));
```

In `MessagingConfiguration.AddFakturennMessaging`, add inside the `UseWolverine` callback:

```csharp
            options.UseEntityFrameworkCoreTransactions();
```

Add `using Wolverine.EntityFrameworkCore;` where required.

- [ ] **Step 2: Write the probe message and handler**

`tests/Fakturenn.IntegrationTests/Messaging/OutboxProbe.cs`:

```csharp
using System.Collections.Concurrent;

namespace Fakturenn.IntegrationTests.Messaging;

/// <summary>
/// A message that exists only to prove the outbox seam.
/// <para>
/// It lives in the test project on purpose. Nothing demonstrative ships in production
/// code, and when E12 publishes something real this probe is not in its way.
/// </para>
/// </summary>
public sealed record OutboxProbeMessage(Guid Id);

public static class OutboxProbe
{
    /// <summary>
    /// Ids the handler actually received. The discriminator between an engaged outbox and
    /// a direct publish is whether the handler ran at all after a rollback -- a direct
    /// publish leaves no envelope row either, because it never writes one.
    /// </summary>
    public static readonly ConcurrentBag<Guid> Handled = [];
}

public sealed class OutboxProbeHandler
{
    public static void Handle(OutboxProbeMessage message) => OutboxProbe.Handled.Add(message.Id);
}
```

- [ ] **Step 3: Write the failing tests**

`tests/Fakturenn.IntegrationTests/Messaging/OutboxTransactionTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests.Messaging;

[Collection(RealHost.Name)]
public sealed class OutboxTransactionTests(SetupHostFixture host)
{
    [Fact]
    public async Task A_rolled_back_transaction_delivers_nothing()
    {
        // THE test. If the enrolment is wrong and publishing goes out directly, a failed
        // invoice still fires its e-invoice -- and the happy path looks perfect.
        var id = Guid.CreateVersion7();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IDbContextOutbox<InvoicesDbContext> outbox =
                scope.ServiceProvider.GetRequiredService<IDbContextOutbox<InvoicesDbContext>>();

            await using IDbContextTransaction transaction =
                await outbox.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

            await outbox.PublishAsync(new OutboxProbeMessage(id));

            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        OutboxProbe.Handled.Should().NotContain(
            id,
            "a message published inside a transaction that aborted must never be delivered");
    }

    [Fact]
    public async Task A_committed_transaction_delivers_and_persists_the_envelope()
    {
        var id = Guid.CreateVersion7();
        long envelopesInFlight;

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IDbContextOutbox<InvoicesDbContext> outbox =
                scope.ServiceProvider.GetRequiredService<IDbContextOutbox<InvoicesDbContext>>();

            await outbox.PublishAsync(new OutboxProbeMessage(id));
            await outbox.SaveChangesAndFlushMessagesAsync();

            // Read before the sender drains it. This is the sliver of durability that is
            // ours: it proves durable storage was selected rather than an in-memory
            // transport, which would pass the delivery assertion below and lose
            // everything on restart.
            envelopesInFlight = await CountOutgoingAsync();
        }

        envelopesInFlight.Should().BeGreaterThan(
            0,
            "the envelope must reach the messaging schema, not sit in memory");

        await WaitForHandledAsync(id);
        OutboxProbe.Handled.Should().Contain(id);
    }

    private async Task<long> CountOutgoingAsync()
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM messaging.wolverine_outgoing_envelopes";
        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WaitForHandledAsync(Guid id)
    {
        for (int attempt = 0; attempt < 40 && !OutboxProbe.Handled.Contains(id); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }
    }
}
```

Confirm the outgoing-envelope table name against the provisioned schema before relying on it — run `\dt messaging.*` against a migrated database and use what is actually there. If the name differs, fix the query and say so in the report rather than working around it.

The probe assembly must be discoverable by Wolverine. If the tests show the handler never runs even on commit, the test assembly is not in the discovery set — add it in the fixture, and note that production discovery is covered separately by Task 4's guard.

- [ ] **Step 4: Run and watch them fail**

Run: `cd /home/christoph/Projects/fakturenn && DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.IntegrationTests --configuration Release --filter-query '/*/*/OutboxTransactionTests/*'`
Expected before Step 1's enrolment is complete: FAIL — `IDbContextOutbox<InvoicesDbContext>` cannot be resolved.

- [ ] **Step 5: Make them pass**

Complete the enrolment from Step 1 and re-run.
Expected: both PASS.

- [ ] **Step 6: Mutate both properties**

Replace `AddDbContextWithWolverineIntegration<InvoicesDbContext>` with the plain `AddDbContext<InvoicesDbContext>`. Expected: `A_rolled_back_transaction_delivers_nothing` reddens, because the publish no longer joins the transaction. Restore.

Replace `PersistMessagesWithPostgresql(...)` with Wolverine's in-memory default (remove the call). Expected: the envelope count assertion reddens. Restore.

Confirm `git status --short src/` is empty after each.

If either mutation leaves everything green, the test is decorative — say so and fix it rather than keeping it.

- [ ] **Step 7: Run everything and commit**

```bash
cd /home/christoph/Projects/fakturenn
git add src/Fakturenn.Web src/Fakturenn.Infrastructure.Messaging tests/Fakturenn.IntegrationTests
git commit --message "feat(messaging): enrol the Invoices context with the transactional outbox

A rolled-back transaction now provably delivers nothing, which is the property
that distinguishes an engaged outbox from a direct publish -- and the failure it
prevents is invisible in the happy path.

The envelope-count assertion is the part of durability that is ours: it proves
durable storage was selected rather than an in-memory transport, which would
pass the delivery test and lose everything on restart.

The probe message and handler live in the test project. Nothing demonstrative
ships.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Guards, conventions and documentation

Turns two conventions into tests, and makes the documentation true. Without this task, adding a module and forgetting to enrol it ships a silently non-transactional publisher.

**Files:**
- Create: `tests/Fakturenn.Web.UnitTests/MessagingCompositionTests.cs`
- Modify: `.claude/CLAUDE.md`
- Modify: `docs/architecture/adr/ADR-007.md`
- Modify: `docs/architecture/IMPLEMENTATION-NOTES.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: everything from Tasks 1–3
- Produces: no code consumed by later work; this task closes the epic

- [ ] **Step 1: Write the host-composition guards**

`tests/Fakturenn.Web.UnitTests/MessagingCompositionTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Host composition, not behaviour. A unit test over a class passes whether or not the
/// class is registered -- which is exactly how the missing AddClaimsPrincipalFactory call
/// hid during E02a until a test asserted the resolved type.
/// </summary>
public sealed class MessagingCompositionTests
{
    [Fact]
    public void The_invoices_context_is_enrolled_with_the_outbox()
    {
        // Enrolment is per-context. A context nobody enrols still publishes --
        // non-transactionally, with no error and no warning -- so a rollback leaves the
        // row gone and the message sent. This is the guard against adding a module and
        // forgetting the step.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        scope.ServiceProvider.GetService<IDbContextOutbox<InvoicesDbContext>>()
            .Should().NotBeNull("every enrolled module context must resolve an outbox");
    }

    [Fact]
    public void Wolverine_discovers_handlers_in_the_module_assemblies()
    {
        // Production discovery is configured and, until E12 publishes something, exercised
        // by nothing else: the integration tests register their own assembly. A
        // misconfigured discovery set would pass every one of them and fail the first time
        // a real handler existed.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        var options = app.Services.GetRequiredService<WolverineOptions>();

        options.Discovery.Assemblies.Should().Contain(
            typeof(InvoicesDbContext).Assembly,
            "handlers in a module assembly must be discovered by the host that runs them");
    }
}
```

Confirm the discovery surface exposed by the restored Wolverine version — if `WolverineOptions.Discovery.Assemblies` is not the shape available, find the equivalent and assert on it rather than deleting the test. A guard that cannot be written is a finding to report, not a step to skip.

If the second test fails because discovery is not configured at all, add the module assemblies to `MessagingConfiguration.AddFakturennMessaging` and say so.

- [ ] **Step 2: Run them**

Run: `cd /home/christoph/Projects/fakturenn && DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.Web.UnitTests --configuration Release`
Expected: 69 passed (67 baseline plus these two).

- [ ] **Step 3: Prove both guards bite**

Replace `AddDbContextWithWolverineIntegration<InvoicesDbContext>` with plain `AddDbContext<InvoicesDbContext>`. Expected: `The_invoices_context_is_enrolled_with_the_outbox` reddens. Restore.

Remove the module assembly from the discovery configuration. Expected: `Wolverine_discovers_handlers_in_the_module_assemblies` reddens. Restore.

Confirm `git status --short src/` is empty after each.

- [ ] **Step 4: Add the enrolment step to the module checklist**

In `.claude/CLAUDE.md`, under "Adding a new module", in the numbered list of conventions, add:

```markdown
9. If the module owns an EF Core `DbContext` that a slice will publish messages
   from, enrol it with the outbox in `FakturennWebApplication.Build` using
   `AddDbContextWithWolverineIntegration<...>` rather than `AddDbContext<...>`.
   Enrolment is per-context: a context nobody enrols still publishes, but
   non-transactionally and with no error, so a rollback leaves the row gone and
   the message sent. `tests/Fakturenn.Web.UnitTests/MessagingCompositionTests.cs`
   guards the contexts enrolled today — extend it when you add one, because
   nothing else will notice.
```

- [ ] **Step 5: Promote ADR-007**

Replace `docs/architecture/adr/ADR-007.md` with:

```markdown
# ADR-007 — Wolverine durable processing

**Status:** Accepted

## Decision

Use Wolverine with PostgreSQL-backed durable local queues and transactional outbox. No external broker for v0.1.

Handlers run **in-process** inside `Fakturenn.Web`. Wolverine owns its own table
definitions in a `messaging` schema; the `--migrate` entrypoint owns when they are
created, and automatic provisioning at startup is deliberately not enabled.

Slices reference Wolverine directly. No architecture rule contains it, because the
precedent is EF Core — which modules already reference without a repository wrapper —
rather than MimeKit and PDFsharp, which are transport and rendering concerns with one
owning adapter each.

## Consequences

One deployment unit, and durability that survives a crash because the envelope is
committed with the rows that caused it. Scheduled work — reminders, backups — needs a
separate answer, because N replicas each firing a timer is not one.

Replacing Wolverine later costs one line per publish call site; handlers and message
types are portable, and the transport configuration is contained in
`Fakturenn.Infrastructure.Messaging`. That estimate is why no abstraction was
introduced. See `docs/superpowers/specs/2026-08-19-wolverine-durable-processing-design.md`.
```

- [ ] **Step 6: Record what was learned**

Add a section to `docs/architecture/IMPLEMENTATION-NOTES.md` covering only what was
actually measured during Tasks 1–3: the real name of Wolverine's outgoing-envelope
table, whether `UseWolverine` sits on `IHostApplicationBuilder` or `IHostBuilder` in
this version, whether test-assembly discovery needed explicit registration, and any
interaction with the integration suite's container count. Write nothing that was not
observed.

- [ ] **Step 7: Update the changelog**

Add under `[Unreleased]` in `CHANGELOG.md`, written for someone using the software:

```markdown
### Added

- Durable background processing. Work that must survive a restart — sending an
  invoice, writing a document — is recorded in the database in the same
  transaction as the change that caused it, so a crash cannot lose it or send it
  twice. Nothing uses this yet; it is the foundation the invoicing and mail
  features will run on.

### Changed

- `--migrate` now also provisions message storage. Take a backup before running
  it: the steps are not rolled back as a group, and a failure part-way names the
  step it failed on and asks you to restore.
```

- [ ] **Step 8: Full verification and commit**

Run every suite, `dotnet build --configuration Release`, `dotnet format --verify-no-changes`.
Expected: unit 31, Identity unit 31, Web unit 69, architecture 14, compliance 10, integration 115, UI 15.

```bash
cd /home/christoph/Projects/fakturenn
git add tests/Fakturenn.Web.UnitTests .claude/CLAUDE.md docs CHANGELOG.md
git commit --message "feat(messaging): guard outbox enrolment and close ADR-007

Enrolment is per-context and silent when missing: an unenrolled context still
publishes, non-transactionally, so a rollback leaves the row gone and the message
sent. That is now a test rather than a sentence in a checklist.

Handler discovery in the production host is configured and, until E12 publishes
something, exercised by nothing else -- the integration tests register their own
assembly. A second guard asserts the module assemblies are in the discovery set.

ADR-007 moves from Proposed to Accepted, carrying the decisions this epic
actually made: in-process, library-owned DDL with our timing, and no abstraction
over Wolverine.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
