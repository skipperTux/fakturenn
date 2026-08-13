using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web;

/// <summary>
/// The PostgreSQL advisory lock that serialises creation of the first administrator.
/// <para>
/// There are two routes to that state — the <c>/setup</c> page and the
/// <c>--create-admin</c> operator entrypoint — and they are alternatives, not a
/// sequence. Each one's "no users exist" query and its insert are not atomic, so
/// without a lock a <c>--create-admin</c> Job racing a <c>/setup</c> post, or two Jobs
/// racing each other, produces two administrators. A unique index does not close this:
/// it only rejects rows colliding on the indexed value, so it serialises two writers
/// using the <i>same</i> e-mail address and does nothing about two using different ones,
/// which is the case that matters (measured in E02a Task 9 — four concurrent posts with
/// distinct addresses produced four administrators).
/// </para>
/// <para>
/// The key lives here rather than at either call site because a <b>different key
/// serialises nothing</b>. Both callers must take this one.
/// </para>
/// </summary>
internal static class SetupLock
{
    /// <summary>
    /// The ASCII bytes of <c>FKTNSETU</c> ("Fakturenn setup") read as a big-endian
    /// 64-bit integer. Derived from text rather than picked at random so it is
    /// reproducible and self-documenting, and it stays inside a positive
    /// <c>bigint</c>.
    /// <para>
    /// Advisory locks share one namespace per database, so this value must be unique
    /// within the database and <b>must never change</b>: a different key is a different
    /// lock, and two application versions taking different keys would not exclude each
    /// other during a rolling deployment.
    /// </para>
    /// </summary>
    private const long Key = 0x464B544E53455455L;

    /// <summary>
    /// Takes the lock on <paramref name="context"/>'s current transaction. Must be the
    /// first statement inside that transaction.
    /// <para>
    /// <c>pg_advisory_xact_lock</c> is transaction-scoped: it releases on commit or
    /// rollback, so there is no cleanup path to forget. It records no state either,
    /// which is why a marker row was rejected — restore a partial backup and a marker
    /// says "configured" while zero users exist, bricking the instance. Zero users must
    /// reopen setup.
    /// </para>
    /// </summary>
    internal static Task TakeAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({Key})", cancellationToken);
    }
}
