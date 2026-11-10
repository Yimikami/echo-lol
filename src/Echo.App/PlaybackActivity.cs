using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private sealed record MatchApm(string Title, DateTimeOffset Date, double Average, double Duration);
    private sealed record Comparison(MatchApm Current, MatchApm[] Previous)
    {
        public double? Baseline => Previous.Length == 0 ? null : Previous.Sum(m => m.Average * m.Duration) / Previous.Sum(m => m.Duration);
    }

    private readonly ConcurrentDictionary<(string Path, long Modified, long Length, double Duration), double> _apmCache = new();
    private Comparison? _apmComparison;

    private void UpdateActivityVisibility()
    {
        bool visible = !_trimOpen || _fullscreen;
        PlaybackActivityPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PlayerDock.RowDefinitions[1].Height = new(visible ? 136 : 22);
        Seek.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetTimelineMoments(ClipItem item)
    {
        var mapped = item.Session.Moments.Select(m => (Time: PlaybackNavigation.FileTime(item.Clip, m.RecordingTime), Moment: m))
            .Where(m => m.Time.HasValue).OrderBy(m => m.Time).ToArray();
        Timeline.Moments = mapped.Select(m => (m.Time!.Value, m.Moment.Kind)).ToArray();
        Timeline.MomentLabels = mapped.GroupBy(m => m.Time!.Value).ToDictionary(group => group.Key,
            group => string.Join(" / ", group.Select(marker => marker.Moment.Label).Distinct()));
    }

    private void RefreshPlaybackActivity()
    {
        PlaybackActivity.Start = Timeline.SelectionStart;
        PlaybackActivity.End = Timeline.SelectionEnd;
        PlaybackActivity.Moments = Timeline.Moments;
        PlaybackActivity.MomentLabels = Timeline.MomentLabels;
        PlaybackActivity.Position = Timeline.Position;
        PlaybackActivity.ReferenceApm = _apmComparison?.Baseline;
        var values = PlaybackActivity.Activity.Where(p => p.Time >= Timeline.SelectionStart && p.Time <= Timeline.SelectionEnd).ToArray();
        if (_apmComparison is { } comparison)
            ApmSummary.Text = comparison.Baseline is { } prior
                ? $"Match {comparison.Current.Average:0} APM   ·   Previous {prior:0} APM"
                : $"Match {comparison.Current.Average:0} APM";
        else
            ApmSummary.Text = values.Length == 0 ? "APM —" : $"Avg {values.Average(p => p.Apm):0} APM   ·   Peak {values.Max(p => p.Apm)} APM";
        ApmSummary.ToolTip = _apmComparison?.Baseline == null ? "APM rate over a rolling 10-second window" : "The dashed line is the duration-weighted average from up to 10 prior matches.";
        PlaybackActivity.InvalidateVisual();
    }

    private async void BeginApmComparison(ClipItem item)
    {
        _apmComparison = null;
        RefreshPlaybackActivity();
        if (!item.WholeMatch || !File.Exists(item.Session.InputLogPath) || StopMediaWork()) return;
        var root = _settings.OutputDirectory;
        var current = new MatchApm(item.Title, item.Session.StartedAt, MatchReview.AverageApm(item.Session, _inputSamples), item.Session.Duration);
        try
        {
            var previous = await Task.Run(() => PreviousApm(root, item.Session));
            if (_closing || _selected?.Entry != item.Entry) return;
            _apmComparison = new(current, previous);
            RefreshPlaybackActivity();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    private MatchApm[] PreviousApm(string root, Session current)
    {
        var earlier = LibraryCatalog.Read(root).Where(s => s.Id != current.Id && s.StartedAt < current.StartedAt && s.EndedAt != null &&
                !s.PreparationPending && s.DeletedAt == null && s.Duration > 0 && File.Exists(s.InputLogPath))
            .OrderByDescending(s => s.StartedAt).Take(10);
        var history = new List<MatchApm>();
        foreach (var match in earlier)
        {
            if (StopMediaWork()) throw new OperationCanceledException();
            var input = new FileInfo(match.InputLogPath!);
            if (!input.Exists) continue;
            var rate = _apmCache.GetOrAdd((input.FullName, input.LastWriteTimeUtc.Ticks, input.Length, match.Duration),
                _ => MatchReview.AverageApm(match, GameplayInputs.Read(input.FullName)));
            history.Add(new(match.Title ?? match.Champion, match.StartedAt, rate, match.Duration));
        }
        return history.ToArray();
    }

    private void ShowApmComparison(ClipItem item)
    {
        if (_selected?.Entry != item.Entry) return;
        if (_apmComparison is not { } comparison)
        {
            BeginApmComparison(item);
            ShowToast("Loading match activity. Open the comparison again in a moment.");
            return;
        }
        var panel = new StackPanel();
        var peak = Math.Max(1, comparison.Previous.Select(m => m.Average).Append(comparison.Current.Average).Max());
        AddRow(comparison.Current, true);
        if (comparison.Baseline is { } baseline)
        {
            panel.Children.Add(new TextBlock { Text = $"Previous {comparison.Previous.Length} matches averaged {baseline:0} APM.", Margin = new(0, 12, 0, 10) });
            foreach (var row in comparison.Previous) AddRow(row, false);
        }
        else panel.Children.Add(new TextBlock { Text = "Earlier matches with recorded inputs will appear here.", Margin = new(0, 12, 0, 0) });
        ShowToolDialog("Match APM", panel, "Done");
        void AddRow(MatchApm row, bool current)
        {
            var title = current ? $"This match · {row.Title}" : $"{row.Date.ToLocalTime():MMM d} · {row.Title}";
            panel.Children.Add(new TextBlock { Text = $"{title}    {row.Average:0} APM", Margin = new(0, 6, 0, 5), TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new ProgressBar { Maximum = peak, Value = row.Average, Height = 5, IsHitTestVisible = false };
            bar.SetResourceReference(ForegroundProperty, "AccentBrush");
            panel.Children.Add(bar);
        }
    }
}
