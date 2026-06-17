namespace WindyCliffs.Drift.Tests.Messaging;

using System;

/// <summary>A controllable clock for deterministic time in tests.</summary>
internal sealed class TestClock(DateTimeOffset now)
{
    public DateTimeOffset Now { get; set; } = now;
}
