using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Web.Components.Account;

public static class AccountEndpoints
{
    /// <summary>
    /// The PostgreSQL advisory-lock key that serialises first-run setup.
    /// <para>
    /// The value is the ASCII bytes of <c>FKTNSETU</c> ("Fakturenn setup") read as a
    /// big-endian 64-bit integer. It is derived from text rather than picked at random
    /// so it is reproducible and self-documenting, and it stays inside a positive
    /// <c>bigint</c>.
    /// </para>
    /// <para>
    /// Advisory locks share one namespace per database, so this key must be unique
    /// within the database and <b>must never change</b>: a different key is a different
    /// lock, and two application versions taking different keys would not exclude each
    /// other during a rolling deployment. Any later operator entrypoint that creates the
    /// first administrator has to take <i>this</i> key.
    /// </para>
    /// </summary>
    private const long SetupLockKey = 0x464B544E53455455L;

    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/account").RequireRateLimiting("account");

        group.MapPost("/setup", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            IdentityDbContext db,
            CancellationToken cancellationToken) =>
        {
            // Read outside the transaction: parsing the request body is not database work
            // and must not hold the advisory lock below.
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string displayName = form["displayName"].ToString().Trim();
            string password = form["password"].ToString();

            // An explicit transaction under a DbContext configured with
            // EnableRetryOnFailure (see IdentityConfiguration) must go through the
            // execution strategy, or EF throws InvalidOperationException. Disabling the
            // retry to avoid this wrapper is not an option -- the retry is deliberate.
            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(
                async token =>
                {
                    await using IDbContextTransaction transaction =
                        await db.Database.BeginTransactionAsync(token);

                    // THE mechanism. The count check and the insert are not atomic, so
                    // without this every concurrent post passes the check and every one
                    // of them becomes an administrator -- measured, four out of four.
                    //
                    // A unique index does NOT serialise this: it only rejects rows that
                    // collide on the indexed value, so it stops two posts using the SAME
                    // e-mail address and does nothing about two posts using different
                    // ones, which is the case that matters. That reasoning was in this
                    // comment before it was tested, and it was wrong.
                    //
                    // pg_advisory_xact_lock is transaction-scoped: it releases on commit
                    // or rollback, so there is no cleanup path to forget. It records no
                    // state either, which is why a marker row was rejected -- restore a
                    // partial backup and a marker says "configured" while zero users
                    // exist, bricking the instance. Zero users must reopen /setup.
                    await db.Database.ExecuteSqlAsync(
                        $"SELECT pg_advisory_xact_lock({SetupLockKey})", token);

                    // Re-checked server-side. The page's own guard is a redirect for
                    // humans; this is the one that actually closes the endpoint, and
                    // under the lock it is now a genuine check-and-act.
                    if (await db.Users.AnyAsync(token))
                    {
                        return Results.NotFound();
                    }

                    var user = new ApplicationUser
                    {
                        Id = Guid.CreateVersion7(),
                        UserName = email,
                        Email = email,
                        DisplayName = displayName,
                        CreatedAt = DateTimeOffset.UtcNow,
                        MustEnrolTotp = true,
                    };

                    IdentityResult created;
                    try
                    {
                        // Password hashing happens inside the lock. That serialises
                        // concurrent first-run posts by roughly one hash each, which is
                        // acceptable on an endpoint that succeeds exactly once per
                        // installation and sits behind the "account" rate limiter.
                        created = await users.CreateAsync(user, password);
                    }
                    catch (DbUpdateException)
                    {
                        // Belt and braces, not the mechanism. The advisory lock excludes
                        // anything that takes the same key; this still catches a writer
                        // that does not -- an operator entrypoint creating an
                        // administrator from another connection, say -- but only when the
                        // user names collide. Task 14 must take SetupLockKey rather than
                        // rely on this.
                        return Results.Redirect("/account/login");
                    }

                    if (!created.Succeeded)
                    {
                        // Identity reports a duplicate user name as a validation failure
                        // rather than an exception, so the same unguarded writer can
                        // surface either way depending on how the store is configured.
                        if (created.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
                        {
                            return Results.Redirect("/account/login");
                        }

                        string message = string.Join(" ", created.Errors.Select(e => e.Description));
                        return Results.Redirect($"/setup?error={Uri.EscapeDataString(message)}");
                    }

                    await RoleSeeder.SeedAsync(db, token);

                    Guid administratorRoleId = await db.Roles
                        .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
                        .Select(role => role.Id)
                        .SingleAsync(token);

                    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRoleId });
                    await db.SaveChangesAsync(token);

                    await transaction.CommitAsync(token);

                    return Results.Redirect("/account/login");
                },
                cancellationToken);
        });
    }
}
