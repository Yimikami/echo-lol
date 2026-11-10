namespace Echo.Core;

/// <summary>Bounded replay segments, including an arbitrarily long chain of overlapping events.</summary>
public sealed class ReplayScheduler(EchoSettings settings)
{
    private readonly List<ClipRange> _pending = [];
    public IReadOnlyList<ClipRange> Pending => _pending;
    public void Add(Moment moment)
    {
        if (!settings.Captures(moment.Kind)) return;
        var rule = settings.Rule(moment.Kind);
        var range = new ClipRange(Math.Max(0, moment.RecordingTime - rule.Before), moment.RecordingTime + rule.After, [moment]);
        var index = _pending.FindIndex(p => p.End >= range.Start);
        if (index < 0) index = _pending.Count;
        while (index < _pending.Count && _pending[index].Start <= range.End)
        {
            var overlap = _pending[index];
            range = new(Math.Min(overlap.Start, range.Start), Math.Max(overlap.End, range.End), [..overlap.Moments, ..range.Moments]);
            _pending.RemoveAt(index);
        }
        _pending.Insert(index, range);
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
