namespace Echo.App;

public partial class MainWindow
{
    private void VerifyPlaybackScrubbing(Action<string, bool> check)
    {
        SetPlaying(false);
        var before = Player.Position;
        BeginScrub();
        for (int i = 0; i < 20000; i++) SeekTo(_duration * ((i % 1000) / 1000d));
        var target = _duration * .371;
        SeekTo(target);
        check("rapid timeline dragging does not seek the decoder for every mouse event", Player.Position == before);
        check("dragging suspends playback input polling", _seeking && !_inputTimer.IsEnabled && _scrubTimer.IsEnabled);
        PreviewScrub();
        check("the drag playhead displays the latest request", Math.Abs(Timeline.Position - target) < .001);
        Tick();
        check("playback polling cannot pull the playhead away from the dragged position", Math.Abs(Timeline.Position - target) < .001);
        CompleteScrub();
        check("release flushes the exact final target and stops the scrub timer", !_seeking && !_scrubTimer.IsEnabled && Math.Abs(Timeline.Position - target) < .001);
        check("scrubbing a paused clip leaves it paused and allows frame previews", !_playing && Player.ScrubbingEnabled);
        SetPlaying(true);
        BeginScrub(); SeekTo(_duration * .22); CompleteScrub();
        check("scrubbing a playing clip resumes playback after release", _playing && !_seeking);
        SetPlaying(false);
        BeginScrub(); SeekTo(_duration * .65);
        CancelScrub();
        var cancelledAt = Timeline.Position;
        CompleteScrub(); PreviewScrub();
        check("cancelled navigation discards pending seeks without moving the next recording", !_seeking && !_scrubTimer.IsEnabled && Timeline.Position == cancelledAt);
        BeginScrub(); SeekTo(_duration * .84);
        BackToLibraryClick(this, new());
        check("leaving playback cancels dragging and its timer", !_seeking && !_scrubTimer.IsEnabled && !_playing);
        OpenEditor();
        SeekTo(Timeline.SelectionStart);
        check("a direct click still seeks immediately", Timeline.Position == Timeline.SelectionStart && !_seeking);
    }
}
