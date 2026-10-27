namespace Echo.Core;

/// <summary>Bounded replay segments, including an arbitrarily long chain of overlapping events.</summary>
public sealed class ReplayScheduler(EchoSettings settings)
{
    private readonly List<ClipRange> _pending = [];
    public IReadOnlyList<ClipRange> Pending => _pending;
    public void Add(Moment moment)
    {
        if (!settings.Captures(moment.Kind)) return;
        var range = new ClipRange(Math.Max(0, moment.RecordingTime - settings.BeforeSeconds), moment.RecordingTime + settings.AfterSeconds, [moment]);
        if (_pending.Count > 0 && range.Start <= _pending[^1].End)
        {
            var last = _pending[^1];
            _pending[^1] = new(Math.Min(last.Start, range.Start), Math.Max(last.End, range.End), [..last.Moments, moment]);
        }
        else _pending.Add(range);
    }
    public ClipRange? TakeDue(double now, bool force = false)
    {
        if (_pending.Count == 0) return null;
        var range = _pending[0];
        if (!force && !MomentPlanner.ShouldSave(now, range, 0, settings.BufferSeconds)) return null;
        var end = Math.Min(now, range.End);
        if (end <= range.Start) { if (force) _pending.RemoveAt(0); return null; }
        var result = new ClipRange(range.Start, end, range.Moments.Where(m => m.RecordingTime <= end).ToList());
        if (range.End <= now || force) _pending.RemoveAt(0);
        else _pending[0] = new(Math.Max(range.Start, end - 1), range.End, range.Moments.Where(m => m.RecordingTime > end - 1).ToList());
        return result;
    }
}
