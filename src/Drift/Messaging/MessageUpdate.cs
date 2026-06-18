namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Describes a change to a leased message applied through
/// <see cref="IMessageLease{TPayload}.UpdateAsync"/>. Every field is optional: a
/// field left <see langword="null"/> leaves the corresponding part of the message
/// unchanged.
/// </summary>
/// <remarks>
/// Payloads are expected to be reference types; <see langword="null"/> is the
/// sentinel for "payload unchanged". A value-type payload has no null sentinel, so
/// it cannot represent the unchanged state — omitting it applies
/// <c>default(TPayload)</c> rather than leaving the payload as it was.
/// </remarks>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed record MessageUpdate<TPayload>
    where TPayload : notnull
{
    private readonly IReadOnlyList<string>? tags;

    /// <summary>The new payload, or <see langword="null"/> to leave the payload unchanged.</summary>
    public TPayload? Payload { get; init; }

    /// <summary>
    /// The new expiry to set, or <see langword="null"/> to leave the current expiry
    /// unchanged. Clearing an existing expiry is not supported in this version.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The new visibility time to set, or <see langword="null"/> to leave the current
    /// value unchanged. Clearing an existing value is not supported in this version.
    /// </summary>
    public DateTimeOffset? InvisibleBefore { get; init; }

    /// <summary>
    /// The new tags to set (replacing the existing tags), or <see langword="null"/> to
    /// leave the tags unchanged. Must not contain null elements.
    /// </summary>
    public IReadOnlyList<string>? Tags
    {
        get => this.tags;
        init => this.tags = TagValidation.EnsureNoNullElements(value, nameof(value));
    }
}
