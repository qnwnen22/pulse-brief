namespace PulseBrief;

public sealed class ApplicationLifetimeInfo
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}
