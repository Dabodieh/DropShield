namespace DropShield.Tests.Support;

internal sealed class TestTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _current = initial;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_sync)
        {
            _current += duration;
        }
    }
}
