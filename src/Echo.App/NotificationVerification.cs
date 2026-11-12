using System.IO;
using System.Reflection;
using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private async Task VerifyNotifications(string output, Action<string, bool> check, bool render)
    {
        LibraryClick(this, new());
        const string detail = "Access to the path 'D:\\Recordings\\Echo\\match\\session.json' is denied.";
        ObserveError("verify", "Couldn't prepare recording", detail, "Retry preparation", () => ShowToast("Preparation queued."));
        check("errors use a persistent toast instead of a header banner", FindName("ErrorBanner") == null &&
            Toast.Visibility == Visibility.Visible && !_toastTimer.IsEnabled && ToastActionButton.IsEnabled);
        ToastDetailsClick(this, new());
        check("notification details retain the complete destination path", ToastDetails.Text == detail && ToastDetailsPanel.Visibility == Visibility.Visible);
        ToastDetailsClick(this, new());
        var log = new FileInfo(Path.Combine(_paths.Root, "ui-errors.log")).Length;
        DismissToastClick(this, new());
        for (int i = 0; i < 20; i++) ObserveError("verify", "Couldn't prepare recording", detail);
        check("dismissed background errors neither reopen nor flood the log", Toast.Visibility == Visibility.Collapsed && new FileInfo(Path.Combine(_paths.Root, "ui-errors.log")).Length == log);
        ClearObservedError("verify");
        ObserveError("verify", "Couldn't prepare recording", detail, "Retry preparation", () => ShowToast("Preparation queued."));
        check("a recovered error can notify again on a new occurrence", Toast.Visibility == Visibility.Visible);
        if (render)
        {
            ThemeService.Apply("Dark");
            await RenderPreview(Path.Combine(output, "notification-dark.png"), new(1480, 920));
            ThemeService.Apply("Light");
            await RenderPreview(Path.Combine(output, "notification-light.png"), new(1100, 720));
            ThemeService.Apply("Dark");
            ToastDetailsClick(this, new());
            await RenderPreview(Path.Combine(output, "notification-details.png"), new(1100, 720));
            var bounds = Toast.TransformToAncestor(ShellRoot).TransformBounds(new Rect(Toast.RenderSize));
            check("expanded notification stays inside the compact window", bounds.Left >= 0 && bounds.Top >= 0 && bounds.Bottom <= ShellRoot.ActualHeight);
            var detailsBounds = ToastDetailsPanel.TransformToAncestor(Toast).TransformBounds(new Rect(ToastDetailsPanel.RenderSize));
            check("expanded details and copy action fit inside the toast", ToastDetailsPanel.ActualHeight > 70 && detailsBounds.Bottom < Toast.ActualHeight);
        }
        ToastActionClick(this, new());
        check("notification action runs and informational messages expire", ToastText.Text == "Preparation queued." && !_toastPersistent && _toastTimer.IsEnabled);
        DismissToastClick(this, new()); ClearObservedError("verify");
        if (render && _items.FirstOrDefault(i => i.IsReady) is { } playable)
        {
            OpenRecording(playable); PrepareVerificationPreview();
            ShowError("Couldn't export", new IOException(detail));
            RecapProgressPanel.Visibility = Visibility.Visible;
            await RenderPreview(Path.Combine(output, "notification-player.png"), new(1100, 720));
            var toastBounds = Toast.TransformToAncestor(ShellRoot).TransformBounds(new Rect(Toast.RenderSize));
            var progressBounds = RecapProgressPanel.TransformToAncestor(ShellRoot).TransformBounds(new Rect(RecapProgressPanel.RenderSize));
            var playerBounds = PlayerControls.TransformToAncestor(ShellRoot).TransformBounds(new Rect(PlayerControls.RenderSize));
            check("notifications do not overlap recap progress or playback controls", progressBounds.Bottom <= toastBounds.Top && toastBounds.Bottom <= playerBounds.Top);
            RecapProgressPanel.Visibility = Visibility.Collapsed;
            DismissToastClick(this, new()); BackToLibraryClick(this, new());
        }

        var paths = new HostPaths(Path.Combine(output, "preparation-profile"));
        var recordings = Path.Combine(paths.Root, "recordings");
        JsonStore.Write(paths.Settings, new EchoSettings { OutputDirectory = recordings });
        var probe = new MainWindow(paths, false);
        probe._timer.Stop();
        try
        {
            var session = new Session { Mode = RecordingMode.FullMatch, Champion = "Fixture", RecordingPath = Path.Combine(recordings, "missing.mkv") };
            var metadata = Path.Combine(recordings, session.Id, "session.json");
            JsonStore.Write(metadata, session);
            probe._matches = true; probe.RefreshCatalog(true);
            var previousItems = probe._items.ToArray(); var previousSignature = probe._catalogSignature;
            // Changed metadata plus an exclusive reader reproduces a refresh failure.
            session.Champion = "Updated fixture";
            JsonStore.Write(metadata, session);
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                probe.RefreshCatalog();
                check("failed catalog read preserves its previous items and signature", probe._items.SequenceEqual(previousItems) && probe._catalogSignature == previousSignature);
            }
            probe.RefreshCatalog();
            check("a subsequent catalog tick recovers without another metadata change", probe._items.Single().Session.Champion == "Updated fixture" && probe._catalogSignature != previousSignature);

            var final = session with { EndedAt = DateTimeOffset.UtcNow, Duration = 120, PreparationPending = true };
            final.Moments.Add(new(1, MomentKind.Kill, 18, 18));
            var queue = Path.Combine(paths.Root, "preparation", session.Id + ".json");
            JsonStore.Write(queue, new PreparationJob(metadata, FinalizedSession: final));
            var recorder = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Echo.Recorder.dll"));
            var take = recorder.GetType("Echo.Recorder.MediaPreparation")!.GetMethod("TakeOne", BindingFlags.Public | BindingFlags.Static)!;
            var recover = recorder.GetType("Echo.Recorder.Supervisor")!.GetMethod("RecoverInterruptedSession", BindingFlags.NonPublic | BindingFlags.Static)!;
            JsonStore.Write(paths.Status, new RecorderStatus { SessionId = session.Id, ElapsedSeconds = 119 });
            var queuedBytes = File.ReadAllBytes(queue);
            recover.Invoke(null, [paths]);
            check("worker recovery preserves an already finalized queued session", File.ReadAllBytes(queue).SequenceEqual(queuedBytes) && JsonStore.Read<Session>(metadata)!.EndedAt == null);
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                await Task.Run(() => take.Invoke(null, [paths, (Func<bool>)(() => false)]));
            var failure = JsonStore.Read<PreparationJob>(queue + ".failed");
            check("a protected metadata file retains the error and final session in the profile", !File.Exists(queue) && failure?.Error?.Contains("session.json") == true &&
                failure.FinalizedSession?.Moments.Count == 1 && JsonStore.Read<Session>(metadata)!.EndedAt == null);
            probe.RefreshCatalog(true);
            var item = probe._items.Single();
            check("failed preparation shows Needs attention even if session metadata could not be updated", item.StateLabel == "Needs attention" && item.Session.EndedAt != null && probe.ToastActionButton.Visibility == Visibility.Visible);
            var retainedBytes = File.ReadAllBytes(metadata);
            probe.ToastActionClick(this, new());
            check("retry queues the retained final snapshot without editing recording metadata", JsonStore.Read<PreparationJob>(queue)?.FinalizedSession?.Moments.Count == 1 && File.ReadAllBytes(metadata).SequenceEqual(retainedBytes));
            await Task.Run(() => take.Invoke(null, [paths, (Func<bool>)(() => false)]));
            var completed = JsonStore.Read<Session>(metadata)!;
            check("preparation recovers after access returns without losing end time or events", File.Exists(queue + ".done") && !File.Exists(queue + ".failed") &&
                !completed.PreparationPending && completed.Error == null && completed.EndedAt == final.EndedAt && completed.Moments.Count == 1);
            probe.RefreshCatalog(true);
            check("successful preparation clears the failure badge", probe._items.All(i => !i.PreparationFailed) && !probe._observedErrors.ContainsKey("preparation:" + session.Id));
            var missing = Path.Combine(recordings, "not-found", "session.json");
            var missingJob = Path.Combine(paths.Root, "preparation", "missing.json");
            JsonStore.Write(missingJob, new PreparationJob(missing));
            await Task.Run(() => take.Invoke(null, [paths, (Func<bool>)(() => false)]));
            check("unavailable metadata is a retained failure rather than a completed job", File.Exists(missingJob + ".failed") && !File.Exists(missingJob + ".done"));
        }
        finally { probe.Close(); ThemeService.Apply(_settings.Theme); }
    }
}
