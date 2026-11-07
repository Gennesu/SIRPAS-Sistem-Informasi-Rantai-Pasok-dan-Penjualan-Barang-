namespace Platform.Tests.TestSupport;

/// <summary>Jam palsu yang dapat dimajukan untuk menguji batas waktu tanpa menunggu.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset? start = null)
    {
        _now = start ?? DateTimeOffset.UtcNow;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
