using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private readonly PlaybackScrub _scrub = new();
    private readonly DispatcherTimer _scrubTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _seeking => _scrub.Active;
    private bool _scrubRangeDirty;
    private static double ScrubNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void BeginScrub()
    {
        if (_closing || _selected == null || _duration <= 0 || _seeking) return;
        _scrub.Begin(Math.Clamp(Timeline.Position, 0, _duration));
        Player.Pause();
        RefreshInputTimer();
        _scrubTimer.Start();
    }

    private void PreviewScrub()
    {
        if (!_seeking || _closing) return;
        FlushScrubRange();
        // UI work is bounded to one update per timer tick, regardless of mouse polling rate.
        if (Timeline.Position != _scrub.Position) UpdateSeekDisplay(_scrub.Position);
        if (_scrub.TakePreview(ScrubNow) is { } target) ApplyMediaSeek(target);
    }

    private void CompleteScrub()
    {
        _scrubTimer.Stop();
        if (_scrub.Complete() is not { } target || _closing || _selected == null) return;
        FlushScrubRange();
        ApplyMediaSeek(target);
        UpdateSeekDisplay(target);
        SetPlaying(_playing);
    }

    private void CancelScrub()
    {
        _scrubTimer.Stop();
        _scrub.Cancel();
        if (!_closing) FlushScrubRange();
        _scrubRangeDirty = false;
    }

    private void FlushScrubRange()
    {
        if (!_scrubRangeDirty) return;
        _scrubRangeDirty = false;
        UpdateRange();
    }

    private void ApplyMediaSeek(double target)
    {
        Player.Position = TimeSpan.FromSeconds(target);
        PlayerPoster.Visibility = Visibility.Collapsed;
    }

    private void UpdateSeekDisplay(double target)
    {
        Timeline.Position = target;
        Timeline.InvalidateVisual();
        PlaybackActivity.Position = target;
        PlaybackActivity.InvalidateVisual();
        _updating = true;
        Seek.Value = target;
        _updating = false;
        UpdatePlaybackTime(target);
        UpdateInputState(target);
    }
}
