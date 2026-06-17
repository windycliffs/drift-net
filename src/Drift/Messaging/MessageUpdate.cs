namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Describes a change to a leased message applied through
/// <see cref="IMessageLease{TPayload}.UpdateAsync"/>.
/// </summary>
/// <param name="Payload">
/// The new payload. The payload is always restated; to change only properties,
/// pass the current payload (available via <see cref="IMessageLease{TPayload}.Message"/>).
/// </param>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed record MessageUpdate<TPayload>(TPayload Payload)
    where TPayload : notnull
{
    /// <summary>
    /// The new expiry to set, or <see langword="null"/> to leave the current expiry
    /// unchanged. Clearing an existing expiry is not supported in this version.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
