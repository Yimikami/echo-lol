namespace Echo.Core;

/// <summary>Keeps one target during a drag; never queues intermediate decoder seeks.</summary>
public sealed class PlaybackScrub
{
    public const double PreviewDelay = .12;
    public bool Active { get; private set; }
    public double Position { get; private set; }
    private bool _pending;
    private double _requestedAt;

    public void Begin(double position)
    {
        Active = true;
        Position = position;
        _pending = false;
    }

    public void Request(double position, double now)
    {
        if (!Active || !double.IsFinite(position) || !double.IsFinite(now) || position == Position) return;
        Position = position;
        _requestedAt = now;
        _pending = true;
    }

    // Preview after the pointer settles. A continuous drag only moves the playhead;
    // MediaElement has no seek-completed signal with which to limit in-flight decoding.
    public double? TakePreview(double now)
    {
        if (!Active || !_pending || now - _requestedAt < PreviewDelay) return null;
        _pending = false;
        return Position;
    }

    public double? Complete()
    {
        if (!Active) return null;
        var result = Position;
        Cancel();
        return result;
    }

    public void Cancel() { Active = false; _pending = false; }
}
