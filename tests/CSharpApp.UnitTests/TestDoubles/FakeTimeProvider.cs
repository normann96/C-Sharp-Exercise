namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>A clock the test moves by hand, so an expiry window can be crossed without waiting for it.</summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    // Interlocked rather than a plain field: the concurrency tests read this clock from many threads at once.
    private long _ticks = now.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
