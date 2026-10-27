using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    internal async Task VerifyUi(string output, bool render = true)
    {
        var checks = new List<string>();
        void Check(string name, bool pass) { if (!pass) throw new InvalidOperationException(name); checks.Add("PASS " + name); }
        var startupCommands = await VerifyStartupCommands(Path.Combine(output, "startup-profile"), false);
        Check("opening Echo resumes healthy paused capture without pressing Record", startupCommands.SequenceEqual(["status", "arm"]));
        var faultCommands = await VerifyStartupCommands(Path.Combine(output, "fault-profile"), true);
        Check("opening Echo preserves an actionable native fault instead of a crash loop", faultCommands.SequenceEqual(["status"]));
        RefreshCatalog(true);
        if (_selected != null)
        {
            _duration = Math.Max(.1, _selected.Clip.End);
            Timeline.Duration = _duration; Timeline.SelectionStart = _selected.Clip.Start; Timeline.SelectionEnd = _selected.Clip.End;
            Timeline.Moments = _selected.Session.Moments.Select(m => (m.RecordingTime - _selected.Clip.RecordingOrigin, m.Kind)).ToArray();
            _updating = true; Seek.Maximum = _duration; _updating = false; UpdateRange();
            PlayerPoster.Source = _selected.Thumbnail; PlayerPoster.Visibility = Visibility.Visible;
            Check("library loads actual clip metadata", ClipList.Items.Count > 0);
            VolumeSlider.Value = .35; Check("volume slider updates media volume", Math.Abs(Player.Volume - .35) < .001);
            MuteClick(this, new()); Check("mute toggles playback", Player.IsMuted);
            MuteClick(this, new()); Check("unmute toggles playback", !Player.IsMuted);
            SetFullscreen(true); Check("fullscreen expands player and removes navigation", _fullscreen && Sidebar.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(LibraryPanel) == 2);
            SetFullscreen(false); Check("fullscreen exits to editor", !_fullscreen && Sidebar.Visibility == Visibility.Visible);
            var marker = _selected.Session.Moments.FirstOrDefault(m => m.RecordingTime - _selected.Clip.RecordingOrigin >= Timeline.SelectionStart && m.RecordingTime - _selected.Clip.RecordingOrigin <= Timeline.SelectionEnd);
            if (marker != null)
            {
                PreviewMoment(marker.RecordingTime);
                Check("event preview seeks before the event", Math.Abs(Timeline.Position - PlaybackNavigation.BeforeEvent(marker.RecordingTime, _selected.Clip.RecordingOrigin, Timeline.SelectionStart, Timeline.SelectionEnd)) < .01);
                SetPlaying(false);
            }
            DeleteClick(this, new()); Check("delete presents protected confirmation", _deleteCandidate != null && DeleteOverlay.Visibility == Visibility.Visible);
            CancelDeleteClick(this, new()); Check("cancel deletion keeps the clip", _deleteCandidate == null && File.Exists(_selected.Clip.Path));
            PlayerPoster.Visibility = Visibility.Visible;
            await LoadFilmstrip(_selected.Clip.Path, _duration, _selectionVersion);
        }
        foreach (var size in render ? new[] { new Size(1480, 920), new Size(1100, 720) } : [])
        {
            LibraryClick(this, new());
            await RenderPreview(Path.Combine(output, $"library-{size.Width}.png"), size);
            Check($"editor fits {size.Width}px", Editor.ActualWidth >= 500 && TrimWorkbench.ActualWidth >= 500 && VideoSurface.ActualHeight >= 140);
            SettingsClick(this, new());
            await RenderPreview(Path.Combine(output, $"settings-{size.Width}.png"), size);
            Check($"settings controls fit {size.Width}px", ModeBox.ActualWidth > 200 && CaptureMethodBox.ActualWidth > 200);
        }
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
    }
    private static async Task<List<string>> VerifyStartupCommands(string profile, bool fault)
    {
        var paths = new HostPaths(profile); var commands = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        async Task Serve()
        {
            for (int i = 0; i < (fault ? 1 : 2); i++)
            {
                await using var server = new NamedPipeServerStream(paths.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(timeout.Token);
                using var reader = new StreamReader(server, leaveOpen: true); var command = await reader.ReadLineAsync(timeout.Token); commands.Add(command ?? "");
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(new RecorderStatus { Armed = command == "arm", Error = fault ? "Native worker stopped" : null }, JsonStore.Options).ReplaceLineEndings(""));
            }
        }
        var service = Serve(); var probe = new MainWindow(paths, false);
        try { await probe.EnsureRecorder(true); await service; }
        catch (Exception ex) { throw new InvalidOperationException($"Startup pipe check: commands={string.Join(',', commands)}; state={probe._status.State}; error={probe.ErrorBanner.Text}", ex); }
        finally { probe.Close(); }
        return commands;
    }
    private async Task RenderPreview(string path, Size size)
    {
        var root = (FrameworkElement)Content;
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
