using System.Data.Common;
using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests.Messaging;

/// <summary>
/// The three properties of the outbox seam that are ours rather than Wolverine's: a
/// rollback delivers nothing, a commit delivers, and the envelope reaches the messaging
/// schema instead of sitting in memory.
/// <para>
/// Borrows the collection fixture's host and its PostgreSQL container. Building a second
/// host here would cost a twelfth concurrent container, and it would take the fixture's
/// logging down with it — see <see cref="MessagingStartupTests"/> for that mechanism.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class OutboxTransactionTests(SetupHostFixture host)
{
    /// <summary>
    /// The inbox, not the outbox table the epic's plan assumed.
    /// <para>
    /// <c>EnvelopeTransactionExtensions.PersistAsync</c> routes an envelope whose destination
    /// scheme is <c>local</c> to <c>PersistIncomingAsync</c>, and every message this
    /// application queues today goes to a durable <b>local</b> queue. So
    /// <c>wolverine_outgoing_envelopes</c> stays empty and the row lands here. Verified
    /// against the schema <c>MessagingStorage.ProvisionAsync</c> creates, and against the
    /// SQL the host actually executed.
    /// </para>
    /// </summary>
    private const string EnvelopeTable = "messaging.wolverine_incoming_envelopes";

    /// <summary>
    /// THE test. If the enrolment is wrong and publishing goes out directly, a failed
    /// invoice still fires its e-invoice — and the happy path looks perfect.
    /// <para>
    /// The two envelope counts carry the transactional claim; "the handler never ran" on its
    /// own does not, and it was measured rather than assumed. Committing the transaction
    /// without flushing leaves the delivery assertion green too, because an outbox delivers
    /// on the flush that follows a commit, not on the commit. What separates an engaged
    /// outbox from a direct publish is that <c>PublishAsync</c> writes a row instead of
    /// sending — so the row appearing inside the transaction and vanishing with it is the
    /// evidence, and the undelivered handler is its consequence.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_rolled_back_transaction_delivers_nothing()
    {
        var rolledBack = Guid.CreateVersion7();
        var control = Guid.CreateVersion7();

        long envelopesInAbortedTransaction = await PublishAsync(rolledBack, commit: false);

        // Not the point of the test, but it is what makes the point meaningful: the
        // envelope really was written, and the rollback is what removed it. Without this,
        // "nothing was delivered" would also be satisfied by nothing ever being published.
        envelopesInAbortedTransaction.Should().Be(
            1,
            "the envelope has to exist inside the transaction before aborting it can prove "
            + "anything");

        long envelopesAfterRollback = await CountCommittedEnvelopesAsync(rolledBack);

        envelopesAfterRollback.Should().Be(
            0,
            "the envelope must go with the transaction that wrote it");

        // The negative assertion below is anchored on a positive one rather than on a
        // delay. A sleep proves only that delivery was not fast; waiting for a message
        // published *after* the rolled-back one proves the queue has drained past the point
        // where a leaked envelope would have appeared. Ordering holds because both go to
        // the same local queue and the rolled-back publish happened first, so anything it
        // leaked is ahead of the control message in it.
        await PublishAsync(control, commit: true);
        await WaitForHandledAsync(control);

        OutboxProbe.Handled.Should().Contain(
            control,
            "the control message anchors the negative assertion below -- without it, "
            + "'not handled yet' and 'never handled' are the same green");

        OutboxProbe.Handled.Should().NotContain(
            rolledBack,
            "a message published inside a transaction that aborted must never be delivered");
    }

    [Fact]
    public async Task A_committed_transaction_delivers_and_persists_the_envelope()
    {
        var id = Guid.CreateVersion7();

        long envelopes = await PublishAsync(id, commit: true);

        // Counted inside the publishing transaction, on its own connection, and filtered to
        // this message's own envelope. Both halves matter: counting the whole table would
        // be satisfied by any other test's row or by a leftover from an earlier run, and
        // counting after the commit would race the durability agent, which deletes the row
        // once the handler has succeeded.
        //
        // This is the sliver of durability that is ours. An in-memory transport would pass
        // the delivery assertion below and lose everything on restart.
        envelopes.Should().Be(
            1,
            "the envelope must reach the messaging schema inside the caller's transaction, "
            + "not sit in memory");

        await WaitForHandledAsync(id);

        OutboxProbe.Handled.Should().Contain(id, "a committed transaction must deliver");
    }

    /// <summary>
    /// Publishes one probe message through the enrolled context's outbox inside an explicit
    /// transaction, and either commits or aborts it. Returns how many envelopes carrying
    /// <paramref name="id"/> were visible inside that transaction before it ended.
    /// </summary>
    private async Task<long> PublishAsync(Guid id, bool commit)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        IDbContextOutbox<InvoicesDbContext> outbox =
            scope.ServiceProvider.GetRequiredService<IDbContextOutbox<InvoicesDbContext>>();

        // The runtime context is configured with EnableRetryOnFailure, and EF refuses a
        // user-initiated transaction under a retrying execution strategy unless the whole
        // unit of work is handed to the strategy. That is the shape a slice will have to
        // use too, so the test uses it rather than switching off the retries it would
        // otherwise be testing around.
        IExecutionStrategy strategy = outbox.DbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await outbox.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

            await outbox.PublishAsync(new OutboxProbeMessage(id));

            // Publishing enrols the envelope in this context's change tracker rather than
            // sending it -- Wolverine's model customizer maps its envelope tables into every
            // enrolled context's model (excluded from migrations), so SaveChangesAsync is
            // what writes it: on this connection, inside this transaction, in the same
            // round trip a slice's own rows would take. Delivery is a separate step, which
            // is the whole point of an outbox.
            await outbox.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            long envelopes = await CountEnvelopesInTransactionAsync(outbox.DbContext, id);

            if (commit)
            {
                // Commits the transaction opened above and only then hands the envelope to
                // the local queue. Deliberately not two steps: the commit-then-deliver order
                // is Wolverine's, and reimplementing it here would test the test.
                await outbox.SaveChangesAndFlushMessagesAsync(TestContext.Current.CancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            }

            return envelopes;
        });
    }

    /// <summary>
    /// Envelopes carrying <paramref name="id"/>, read on the context's own connection and
    /// inside its current transaction — so uncommitted rows are visible and no other test's
    /// envelope can satisfy the count.
    /// </summary>
    private static Task<long> CountEnvelopesInTransactionAsync(DbContext context, Guid id)
    {
        DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        return CountAsync(command, id);
    }

    /// <summary>Envelopes carrying <paramref name="id"/> that are committed and visible to anyone.</summary>
    private async Task<long> CountCommittedEnvelopesAsync(Guid id)
    {
        await using NpgsqlConnection connection = new(host.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return await CountAsync(connection.CreateCommand(), id);
    }

    private static async Task<long> CountAsync(DbCommand command, Guid id)
    {
        await using (command)
        {
            // The body is the serialised message, so the id is in it verbatim. Filtering on
            // it rather than counting the table is what stops another test's envelope, or a
            // leftover from an earlier run, from satisfying the assertion.
            //
            // Matched as bytes rather than by decoding the column: the same table also holds
            // Wolverine's own control-queue envelopes, whose bodies are not valid UTF-8, and
            // convert_from raises 22021 on the first one it meets.
            command.CommandText =
                $"SELECT count(*) FROM {EnvelopeTable} "
                + "WHERE position(convert_to(@id, 'UTF8') in body) > 0";

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id.ToString();
            command.Parameters.Add(parameter);

            object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            return Convert.ToInt64(count, CultureInfo.InvariantCulture);
        }
    }

    private static async Task WaitForHandledAsync(Guid id)
    {
        for (int attempt = 0; attempt < 40 && !OutboxProbe.Handled.Contains(id); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }
    }
}
