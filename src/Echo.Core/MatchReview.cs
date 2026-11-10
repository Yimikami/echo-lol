namespace Echo.Core;

public static class MatchReview
{
    public static IReadOnlyList<TimeRange> RecapRanges(Session match, IReadOnlySet<MomentKind> kinds, EchoSettings fallback)
    {
        var settings = match.RecordingOptions ?? fallback;
        var ranges = match.Moments.Where(moment => kinds.Contains(moment.Kind) &&
                double.IsFinite(moment.RecordingTime) && moment.RecordingTime >= 0 && moment.RecordingTime <= match.Duration)
            .Select(moment =>
            {
                var rule = settings.Rule(moment.Kind);
                return new TimeRange(Math.Max(0, moment.RecordingTime - rule.Before), Math.Min(match.Duration, moment.RecordingTime + rule.After));
            })
            .OrderBy(range => range.Start);
        var merged = new List<TimeRange>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = Math.Max(range.End, merged[^1].End) };
            else
                merged.Add(range);
        }
        return merged.SelectMany(range => settings.RemoveMaskedHighlights
                ? MaskTimeline.Visible(range.Start, range.End, match.MaskedRanges)
                : [range])
            .Where(range => range.Duration >= .15).ToArray();
    }

    public static double AverageApm(Session match, IReadOnlyList<InputSample> samples)
    {
        if (!double.IsFinite(match.Duration) || match.Duration <= 0) return 0;
        return samples.Count(input => input.Down && input.Time >= 0 && input.Time <= match.Duration) * 60d / match.Duration;
    }
}
