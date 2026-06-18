namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared validation for tag lists.</summary>
internal static class TagValidation
{
    /// <summary>
    /// Returns <paramref name="tags"/> unchanged after verifying it contains no null
    /// elements. A <see langword="null"/> list is allowed and returned as-is.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains a null element.</exception>
    public static IReadOnlyList<string>? EnsureNoNullElements(IReadOnlyList<string>? tags, string paramName)
    {
        if (tags is not null && tags.Any(static tag => tag is null))
        {
            throw new ArgumentException("Tags must not contain null elements.", paramName);
        }

        return tags;
    }
}
