using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fakturenn.Infrastructure.Messaging;

/// <summary>
/// Says once, at startup, that message storage is not durable.
/// <para>
/// Wolverine falls back to in-memory queues when no connection string is configured, and
/// that fallback is deliberate: "not configured yet" is a first-class state for this
/// application, and the database-free UI fixture depends on it. What it may not be is
/// silent. A typo in the <c>ConnectionStrings:Fakturenn</c> key would otherwise produce a
/// host that answers <c>/alive</c> with 200 while losing every queued message on restart
/// -- exactly the failure class the outbox exists to prevent.
/// </para>
/// <para>
/// A hosted service rather than a log call inside <see cref="MessagingConfiguration"/>,
/// because registration runs before any logger exists: <c>IHostApplicationBuilder</c>
/// exposes none, and the <c>UseWolverine</c> configuration lambda runs without one either.
/// This is the earliest point at which the host's real, configured logging pipeline can
/// carry the message.
/// </para>
/// </summary>
internal sealed partial class NonDurableMessagingWarning(ILogger<NonDurableMessagingWarning> logger)
    : IHostedService
{
    // public Methods
    public Task StartAsync(CancellationToken cancellationToken)
    {
        DurablePersistenceNotConfigured(logger);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // private Methods
    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Durable message persistence is not configured: no connection string is set "
            + "for 'Fakturenn'. Messages are held in memory and will not survive a restart. "
            + "Set the connection string and run --migrate to make message processing durable.")]
    private static partial void DurablePersistenceNotConfigured(ILogger logger);
}
