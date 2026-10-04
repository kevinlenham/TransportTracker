namespace TransportTracker.Tests;

/// <summary>A clock stopped at one instant.</summary>
public class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
