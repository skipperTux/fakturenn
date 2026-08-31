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
