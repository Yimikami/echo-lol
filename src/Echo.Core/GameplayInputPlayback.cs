namespace Echo.Core;

// Display pulses are separate from physical press counts.
public sealed class GameplayInputPlayback
{
    private const double MinimumVisibleSeconds = .16;
    private readonly Dictionary<string, TimeRange[]> _pulses = new(StringComparer.Ordinal);
    private readonly double[] _presses;

    public GameplayInputPlayback(IReadOnlyList<InputSample> samples)
    {
        _presses = samples.Where(s => s.Down).Select(s => s.Time).ToArray();
        foreach (var group in samples.GroupBy(s => s.Control))
        {
            var ranges = new List<TimeRange>();
            if (IsWheel(group.Key))
            {
                foreach (var sample in group.Where(s => s.Down)) Add(sample.Time, sample.Time + MinimumVisibleSeconds);
            }
            else
            {
                double? started = null;
                foreach (var sample in group)
                {
                    if (sample.Down) started ??= sample.Time;
                    else if (started is { } start)
                    {
                        Add(start, Math.Max(sample.Time, start + MinimumVisibleSeconds));
                        started = null;
                    }
                }
                if (started is { } remaining) Add(remaining, double.PositiveInfinity);
            }
            _pulses.Add(group.Key, ranges.ToArray());

            void Add(double start, double end)
            {
                if (ranges.Count > 0 && start <= ranges[^1].End)
                    ranges[^1] = ranges[^1] with { End = Math.Max(end, ranges[^1].End) };
                else ranges.Add(new(start, end));
            }
        }
    }

    private static bool IsWheel(string control) => control is "WheelUp" or "WheelDown";

    public IReadOnlyList<TimeRange> Pulses(string control) => _pulses.GetValueOrDefault(control) ?? [];

    public HashSet<string> Visible(double time)
    {
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (control, ranges) in _pulses)
        {
            int index = LastAtOrBefore(ranges, static range => range.Start, time);
            if (index >= 0 && time < ranges[index].End) visible.Add(control);
        }
        return visible;
    }

    public int Apm(double time) =>
        LastAtOrBefore(_presses, static press => press, time) - LastAtOrBefore(_presses, static press => press, time - 60);

    private static int LastAtOrBefore<T>(T[] entries, Func<T, double> timestamp, double time)
    {
        int low = 0, high = entries.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (timestamp(entries[mid]) <= time) low = mid + 1;
            else high = mid;
        }
        return low - 1;
    }
}
