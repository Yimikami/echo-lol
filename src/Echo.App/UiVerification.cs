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
        Check("opening Echo resumes capture without pressing Record", startupCommands.SequenceEqual(["status", "arm"]));
        var faultCommands = await VerifyStartupCommands(Path.Combine(output, "fault-profile"), true);
        Check("native faults stay actionable instead of looping", faultCommands.SequenceEqual(["status"]));
        Check("Phosphor vector assets resolve", TryFindResource("Phosphor.Play") is Geometry);
        Check("settings have no automatic-save status label", FindName("SettingsMessage") == null);
        Check("Manrope is embedded and resolves", FontFamily.GetTypefaces().Any(t => t.TryGetGlyphTypeface(out var glyph) && glyph.FamilyNames.Values.Any(n => n.Contains("Manrope", StringComparison.OrdinalIgnoreCase))));
        RefreshCatalog(true);
        Check("library opens as gallery without forcing the editor", EditorPanel.Visibility == Visibility.Collapsed && LibraryPanel.Visibility == Visibility.Visible);
        if (ClipList.Items.Count > 0)
        {
            ClipList.SelectedIndex = 0; PrepareVerificationPreview();
            Check("watching starts without trim controls", !_trimOpen && Inspector.Visibility == Visibility.Collapsed && TrimWorkbench.Visibility == Visibility.Collapsed);
            Check("normal playback always shows activity navigation", PlaybackActivityPanel.Visibility==Visibility.Visible&&Seek.Visibility==Visibility.Collapsed&&PlaybackActivity.End>PlaybackActivity.Start);
            ToggleTrimClick(this, new()); Check("trim is an explicit optional action", _trimOpen && Inspector.Visibility == Visibility.Visible);
            Check("selecting a real clip opens the editor", _selected != null && EditorPanel.Visibility == Visibility.Visible && LibraryPanel.Visibility == Visibility.Collapsed);
            VolumeSlider.Value = .35; Check("volume controls media volume", Math.Abs(Player.Volume - .35) < .001);
            MuteClick(this, new()); Check("mute toggles playback", Player.IsMuted);
            MuteClick(this, new()); Check("unmute toggles playback", !Player.IsMuted);
            SetFullscreen(true); Check("fullscreen expands only the player workspace", _fullscreen && Sidebar.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(EditorPanel) == 2 && InspectorColumn.Width.Value == 0);
            Check("fullscreen uses an icon without a floating text button",FindName("ExitFullscreenButton")==null&&FullscreenToggleIcon.Kind=="ExitFullscreen");
            SetFullscreen(false); Check("fullscreen restores inspector and timeline", !_fullscreen && Inspector.Visibility == Visibility.Visible && TrimWorkbench.Visibility == Visibility.Visible);
            var marker = _selected!.Session.Moments.FirstOrDefault(m => m.RecordingTime - _selected.Clip.RecordingOrigin >= Timeline.SelectionStart && m.RecordingTime - _selected.Clip.RecordingOrigin <= Timeline.SelectionEnd);
            if (marker != null)
            {
                PreviewMoment(marker.RecordingTime);
                Check("events preview three seconds before action", Math.Abs(Timeline.Position - PlaybackNavigation.BeforeEvent(_selected.Clip, marker.RecordingTime, Timeline.SelectionStart, Timeline.SelectionEnd)) < .01);
                SetPlaying(false);
            }
            DeleteClick(this, new()); Check("deletion is protected by confirmation", _deleteCandidate != null && DeleteOverlay.Visibility == Visibility.Visible);
            CancelDeleteClick(this, new()); Check("cancelling deletion keeps media", _deleteCandidate == null && File.Exists(_selected.Clip.Path));
            PlayerPoster.Visibility = Visibility.Visible;
            await LoadFilmstrip(_selected.Clip.Path, _duration, _selectionVersion);
            BackToLibraryClick(this, new()); Check("back returns to gallery", LibraryPanel.Visibility == Visibility.Visible && EditorPanel.Visibility == Visibility.Collapsed);
        }
        foreach (var size in render ? new[] { new Size(1480, 920), new Size(1100, 720) } : [])
        {
            LibraryClick(this, new()); await RenderPreview(Path.Combine(output, $"gallery-{size.Width}.png"), size);
            Check($"gallery fits {size.Width}px", ClipList.ActualWidth > 800);
            if (ClipList.Items.Count > 0)
            {
                ClipList.SelectedIndex = 0; PrepareVerificationPreview();
                await LoadFilmstrip(_selected!.Clip.Path, _duration, _selectionVersion);
                await RenderPreview(Path.Combine(output, $"watch-{size.Width}.png"), size);
                Check($"normal player hides editing at {size.Width}px", Inspector.Visibility == Visibility.Collapsed && TrimWorkbench.Visibility == Visibility.Collapsed);
                ToggleTrimClick(this, new());
                await RenderPreview(Path.Combine(output, $"editor-{size.Width}.png"), size);
                var track = (System.Windows.Controls.Primitives.Track)VolumeSlider.Template.FindName("PART_Track", VolumeSlider);
                File.WriteAllText(Path.Combine(output, "slider-layout.txt"), $"Value={track.Value} min={track.Minimum} max={track.Maximum} track={track.ActualWidth}x{track.ActualHeight} decrease={track.DecreaseRepeatButton.ActualWidth}x{track.DecreaseRepeatButton.ActualHeight} increase={track.IncreaseRepeatButton.ActualWidth}x{track.IncreaseRepeatButton.ActualHeight}");
                Check($"volume track is visible at {size.Width}px", track.DecreaseRepeatButton.ActualWidth > 2 && track.IncreaseRepeatButton.ActualWidth > 2);
                Check($"editor fits {size.Width}px", VideoSurface.ActualWidth >= 600 && VideoSurface.ActualHeight >= 200 && TrimWorkbench.ActualWidth >= 900);
            }
            SettingsClick(this, new());
            foreach (var category in new[] { "Recording", "Audio", "Highlights", "Storage","Inputs" })
            {
                ShowSettingsCategory(category);
                await RenderPreview(Path.Combine(output, $"settings-{category.ToLowerInvariant()}-{size.Width}.png"), size);
                Check($"{category} settings visible at {size.Width}px", new[] { RecordingSettings, AudioSettings, HighlightsSettings, StorageSettings,InputSettings }.Count(p => p.Visibility == Visibility.Visible) == 1);
                if(category=="Inputs"){var frame=InputPlacementPreview.TransformToAncestor(SettingsScroll).TransformBounds(new Rect(InputPlacementPreview.RenderSize));Check($"entire gameplay HUD preview fits at {size.Width}px",frame.Top>=0&&frame.Bottom<=SettingsScroll.ActualHeight+1);}
            }
        }
        var testPaths = new HostPaths(Path.Combine(output, "settings-profile")); var probe = new MainWindow(testPaths, false);
        try
        {
            probe.BeforeInput.Text = "invalid"; probe.SaveSettingsClick(this, new());
            Check("invalid settings stay visible and are not written", !File.Exists(testPaths.Settings) && probe.SettingsError.Text.Length > 0);
            probe.BeforeInput.Text = "18"; probe.OutputInput.Text = Path.Combine(testPaths.Root, "recordings"); probe.MicBox.IsChecked = false; probe.SaveSettingsClick(this, new());
            Check("valid settings persist to the selected profile", JsonStore.Read<EchoSettings>(testPaths.Settings)?.BeforeSeconds == 18 && probe.SettingsError.Text.Length == 0);
            probe.DesktopAudioBox.IsChecked = true;
            Check("desktop audio toggle saves without a button", JsonStore.Read<EchoSettings>(testPaths.Settings)?.DesktopAudio == true);
            probe.ModeBox.SelectedIndex = 1;
            Check("entire match selection saves immediately", JsonStore.Read<EchoSettings>(testPaths.Settings)?.Mode == RecordingMode.FullMatch);
            probe.ShortcutInput.Text = "Ctrl+Alt+F9"; probe.AutoSave(false);
            Check("manual shortcut is saved with settings", JsonStore.Read<EchoSettings>(testPaths.Settings)?.ManualShortcut == "Ctrl+Alt+F9");
            probe.RecordInputsBox.IsChecked = true;
            Check("gameplay input recording is explicitly opt-in and persists", JsonStore.Read<EchoSettings>(testPaths.Settings)?.RecordInputs == true);
            var match = new Session { Mode = RecordingMode.FullMatch, RecordingPath = Path.Combine(testPaths.Root, "live.mkv") };
            var matchFolder = Path.Combine(probe._settings.OutputDirectory, match.Id); Directory.CreateDirectory(matchFolder); JsonStore.Write(Path.Combine(matchFolder, "session.json"), match);
            probe._matches = true; probe.RefreshCatalog(true);
            Check("in-progress full match is visible before finalization", probe.ClipList.Items.Count == 1 && probe.ClipList.Items.Cast<ClipItem>().Single().StateLabel == "Recording");
            probe._matches = false; probe._items.Clear();
            for (int i = 0; i < 65; i++) probe._items.Add(new(new Session { Champion = i == 0 ? "Unique champion" : "Fixture", StartedAt = DateTimeOffset.UtcNow.AddSeconds(-i) }, new(i.ToString(), "", 0, 10, "Kill", false), null));
            probe.ApplyFilter(); Check("gallery page is bounded to 32 items", probe.ClipList.Items.Count == 32);
            probe.NextPageClick(this, new()); probe.NextPageClick(this, new()); Check("gallery pagination reaches the final item", probe.ClipList.Items.Count == 1);
            probe.SearchBox.Text = "Unique champion"; Check("search resets pagination and filters results", probe.ClipList.Items.Count == 1 && probe._galleryPage == 0);
        }
        finally { probe.Close(); }
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
    }
    private void PrepareVerificationPreview()
    {
        if (_selected == null) return;
        _duration = Math.Max(.1, _selected.Clip.End); Timeline.Duration = _duration; Timeline.SelectionStart = _selected.Clip.Start; Timeline.SelectionEnd = _selected.Clip.End;
        Timeline.Moments = _selected.Session.Moments.Select(m => (Time: PlaybackNavigation.FileTime(_selected.Clip, m.RecordingTime), m.Kind)).Where(m => m.Time.HasValue).Select(m => (m.Time!.Value, m.Kind)).ToArray();
        _updating = true; Seek.Maximum = _duration; _updating = false; UpdateRange();
        PlayerPoster.Source = _selected.Thumbnail; PlayerPoster.Visibility = Visibility.Visible; RefreshStatus();
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
