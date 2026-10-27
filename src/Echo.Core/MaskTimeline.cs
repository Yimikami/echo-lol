namespace Echo.Core;

/// <summary>Tracks explicit capture masking, rather than guessing from dark gameplay pixels.</summary>
public sealed class MaskTimeline
{
    private readonly List<TimeRange> _ranges = [];
    private double? _start;
    public void Update(double time, bool masked)
    {
        time = Math.Max(0, time);
        if (masked) _start ??= time;
        else if (_start is { } start) { if (time > start) _ranges.Add(new(start, time)); _start = null; }
    }
    public List<TimeRange> Snapshot(double end) => _ranges.Concat(_start is { } start && end > start ? [new TimeRange(start, end)] : []).ToList();
    public static List<TimeRange> Visible(double start, double end, IEnumerable<TimeRange> masked, double padding = .06)
    {
        var result = new List<TimeRange>(); var cursor = start;
        foreach (var range in masked.OrderBy(r => r.Start))
        {
            var from = Math.Max(start, range.Start - padding); var to = Math.Min(end, range.End + padding);
            if (to <= cursor || from >= end) continue;
            if (from > cursor + .04) result.Add(new(cursor, from));
            cursor = Math.Max(cursor, to);
        }
        if (end > cursor + .04) result.Add(new(cursor, end));
        return result;
    }
}
