namespace QQListener.Tests.TestSupport;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now.ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public void Advance(TimeSpan value) => UtcNow += value;
}
