namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Describes a change to a leased message's properties applied through
/// <see cref="IMessageLease.UpdateAsync(MessageUpdate, CancellationToken)"/> (or its
/// payload-bearing overload). Every field is optional: a field left
/// <see langword="null"/> leaves the corresponding part of the message unchanged.
/// </summary>
public sealed record MessageUpdate
{
    private readonly IReadOnlyList<string>? tags;

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
