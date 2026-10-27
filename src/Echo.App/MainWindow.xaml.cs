using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shell;
using Echo.Core;
using Echo.Recorder;
using Microsoft.Win32;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using RecycleFile = Microsoft.VisualBasic.FileIO.FileSystem;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using RecycleUI = Microsoft.VisualBasic.FileIO.UIOption;
using RecycleCancel = Microsoft.VisualBasic.FileIO.UICancelOption;

namespace Echo.App;

public partial class MainWindow : Window
{
    private readonly HostPaths _paths;
    private readonly bool _startRecorder;
    private EchoSettings _settings;
    private RecorderStatus _status = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<ClipItem> _items = [];
    private ClipItem? _selected, _deleteCandidate;
    private string _filter = "All", _catalogSignature = "";
    private bool _updating, _seeking, _playing, _matches, _exporting, _fullscreen, _reconnecting, _deleting;
    private volatile bool _closing;
    private double _duration;
    private int _ticks, _selectionVersion;
    private WindowState _priorWindowState;
    private WindowChrome? _priorChrome;
    private ResizeMode _priorResizeMode;
    private readonly SemaphoreSlim _filmstripLock = new(1, 1);

    public MainWindow(HostPaths paths, bool startRecorder)
    {
        _paths = paths; _startRecorder = startRecorder;
        try { _settings = paths.LoadSettings(); }
        catch (Exception) { _settings = new(); }
        InitializeComponent(); LoadSettingsFields(); SetNav("Clips"); SetFilter();
        Player.Volume = VolumeSlider.Value;
        Timeline.SeekRequested += SeekTo;
        Timeline.MomentRequested += t => PreviewMoment(t + (_selected?.Clip.RecordingOrigin ?? 0));
        Timeline.RangeChanged += (_, _) => UpdateRange();
        Loaded += async (_, _) => { if (_startRecorder) await EnsureRecorder(true); RefreshCatalog(true); RefreshStatus(); };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        Closing += (_, _) => { _closing = true; _selectionVersion++; _timer.Stop(); Player.Close(); };
    }
    private void Tick()
    {
        if (++_ticks % 5 == 0) RefreshStatus();
        if (_ticks % 25 == 0)
        {
            RefreshCatalog();
            if (_startRecorder && DateTimeOffset.UtcNow - _status.UpdatedAt > TimeSpan.FromSeconds(8) && !_reconnecting) _ = EnsureRecorder();
        }
        if (_selected == null || _duration <= 0 || _seeking) return;
        var position = Math.Clamp(Player.Position.TotalSeconds, 0, _duration);
        _updating = true; Seek.Value = position; _updating = false;
        Timeline.Position = position; Timeline.InvalidateVisual();
        TimeLabel.Text = $"{Time(position)} / {Time(_duration)}";
        if (_playing && position >= Timeline.SelectionEnd) SetPlaying(false);
    }
    private bool CaptureActive()
    {
        var status = JsonStore.Read<RecorderStatus>(_paths.Status);
        return status != null && DateTimeOffset.UtcNow - status.UpdatedAt < TimeSpan.FromSeconds(8) && (status.Recording || status.State == "Starting recording");
    }
    private void RefreshStatus()
    {
        try
        {
            var status = JsonStore.Read<RecorderStatus>(_paths.Status);
            bool fresh = status != null && DateTimeOffset.UtcNow - status.UpdatedAt < TimeSpan.FromSeconds(8);
            _status = fresh ? status! : new() { State = "Recorder offline", UpdatedAt = DateTimeOffset.MinValue };
            HeaderStatus.Text = _status.State;
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_status.Recording ? "#FFAE92" : fresh && _status.Armed ? "#9DE1C7" : "#ACBAB6"));
            CaptureButton.Content = !fresh ? "Reconnect" : _status.Error != null ? "Retry capture" : _status.Armed ? "Pause capture" : "Resume capture";
            RecorderLabel.Text = _status.Recording ? "Recording" : !fresh ? "Reconnecting…" : _status.Armed ? "Auto capture" : "Capture paused";
            RecorderDetail.Text = _status.Recording ? $"{Time(_status.ElapsedSeconds)} recorded\nGame audio: {(_status.GameAudioHooked == true ? "on" : _status.GameAudioHooked == false ? "waiting" : "off")}" : !fresh ? "Waiting for the recording service." : _status.Armed ? "Play League. Echo handles the rest." : "Resume to save your next moments.";
            ErrorBanner.Text = _status.Error ?? "";
            ExportButton.IsEnabled = _selected != null && !CaptureActive() && !_exporting && _duration > 0;
            ExportButton.ToolTip = CaptureActive() ? "Export is available after the match." : "Save the selected range as a new MP4";
            DeleteButton.IsEnabled = _selected != null && !_exporting;
            RevealButton.IsEnabled = _selected != null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { ErrorBanner.Text = "Could not read recorder status: " + e.Message; }
    }
    private void RefreshCatalog(bool force = false)
    {
        if (_deleteCandidate != null) return;
        try
        {
            if (!Directory.Exists(_settings.OutputDirectory)) { _items.Clear(); ApplyFilter(); return; }
            var files = Directory.EnumerateDirectories(_settings.OutputDirectory).Take(1000).Select(dir => new FileInfo(Path.Combine(dir, "session.json"))).Where(f => f.Exists).ToArray();
            var signature = string.Join(";", files.Select(f => $"{f.Directory!.Name}:{f.LastWriteTimeUtc.Ticks}:{f.Length}"));
            if (!force && signature == _catalogSignature) return;
            _catalogSignature = signature; _items.Clear();
            foreach (var file in files)
            {
                Session? session;
                try { session = JsonStore.Read<Session>(file.FullName); } catch (Exception e) when (e is IOException or JsonException) { continue; }
                if (session == null || session.EndedAt == null) continue;
                if (_matches)
                {
                    if (session.RecordingPath is { } path && File.Exists(path)) _items.Add(new(session, new(session.Id, path, 0, session.Duration, "Full match", true), FindMatchPoster(file.DirectoryName!, session)));
                }
                else foreach (var clip in session.Clips) if (File.Exists(clip.Path)) _items.Add(new(session, clip, Path.Combine(file.DirectoryName!, clip.Id + ".jpg")));
            }
            _items.Sort((a, b) => b.Session.StartedAt.CompareTo(a.Session.StartedAt)); ApplyFilter();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorBanner.Text = "Could not open the recording folder: " + e.Message; }
    }
    private static string? FindMatchPoster(string folder, Session session) => session.Clips.Select(c => Path.Combine(folder, c.Id + ".jpg")).FirstOrDefault(File.Exists);
    private void ApplyFilter()
    {
        if (ClipList == null) return;
        var selectedId = _selected?.Clip.Id; var query = SearchBox.Text.Trim();
        var visible = _items.Where(i => (_filter == "All" || i.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)) && (query.Length == 0 || (i.Title + " " + i.DateLabel + " " + i.Label).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        ClipList.ItemsSource = visible;
        LibraryCount.Text = $"{visible.Length} {(_matches ? "matches" : "clips")}";
        EmptyList.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyList.Text = _items.Count == 0 ? (_matches ? "Your full matches will appear here. Choose Entire match in Settings to save a whole game." : "Your moments will appear here. Play League with Echo running to save clips automatically.") : "No clips match your search. Try another event or search term.";
        if (visible.Length > 0) ClipList.SelectedItem = visible.FirstOrDefault(i => i.Clip.Id == selectedId) ?? visible[0];
        else ClearPlayer();
    }
    private void ClearPlayer()
    {
        _selected = null; _selectionVersion++; SetPlaying(false); Player.Close(); _duration = 0;
        Timeline.Duration = 0; Timeline.Frames = []; Timeline.Moments = []; Timeline.InvalidateVisual();
        PlayerPoster.Source = null; PlayerPoster.Visibility = Visibility.Collapsed; EmptyPlayer.Visibility = Visibility.Visible;
        SelectedTitle.Text = "Keep the moment."; SelectedDetails.Text = "Select a clip to start watching.";
        _updating = true; Seek.Value = 0; _updating = false; TimeLabel.Text = "00:00 / 00:00"; UpdateRange();
    }
    private void ClipSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipItem item) return;
        if (_selected?.Clip.Id == item.Clip.Id && _selected.Clip.Path == item.Clip.Path) { _selected = item; return; }
        _selectionVersion++; _selected = item; _duration = 0; SetPlaying(false); Player.Close(); EmptyPlayer.Visibility = Visibility.Collapsed;
        SelectedTitle.Text = item.Title; SelectedDetails.Text = item.DateLabel + "   ·   " + item.Label;
        Timeline.Frames = item.Thumbnail == null ? [] : [item.Thumbnail]; Timeline.Duration = 0; Timeline.InvalidateVisual();
        PlayerPoster.Source = item.Thumbnail; PlayerPoster.Visibility = item.Thumbnail == null ? Visibility.Collapsed : Visibility.Visible;
        ActionStatus.Text = "Drag the edges to trim. Event previews start 3 seconds before the action.";
        Player.Source = new Uri(item.Clip.Path); Player.Play(); Player.Pause();
    }
    private void MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !Player.NaturalDuration.HasTimeSpan) return;
        _duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
        _updating = true; Seek.Maximum = _duration;
        Timeline.Duration = _duration; Timeline.SelectionStart = Math.Clamp(_selected.Clip.Start, 0, _duration);
        Timeline.SelectionEnd = Math.Clamp(_selected.Clip.End, Timeline.SelectionStart, _duration);
        Timeline.Moments = _selected.Session.Moments.Select(m => (m.RecordingTime - _selected.Clip.RecordingOrigin, m.Kind)).ToArray();
        _updating = false; UpdateRange();
        var first = _selected.Session.Moments.FirstOrDefault(m => m.RecordingTime - _selected.Clip.RecordingOrigin >= Timeline.SelectionStart && m.RecordingTime - _selected.Clip.RecordingOrigin <= Timeline.SelectionEnd);
        SeekTo(first == null ? Timeline.SelectionStart : PlaybackNavigation.BeforeEvent(first.RecordingTime, _selected.Clip.RecordingOrigin, Timeline.SelectionStart, Timeline.SelectionEnd));
        Player.Pause(); RefreshStatus(); _ = LoadFilmstrip(_selected.Clip.Path, _duration, _selectionVersion);
    }
    private async Task LoadFilmstrip(string source, double duration, int version)
    {
        // Decode at most eight small frames, serially, only outside active capture. No background library-wide scan.
        if (CaptureActive()) return;
        await _filmstripLock.WaitAsync();
        try
        {
            if (_closing || version != _selectionVersion || CaptureActive()) return;
            var frames = new List<ImageSource>();
            for (int i = 0; i < 8; i++)
            {
                if (_closing || version != _selectionVersion || CaptureActive()) return;
                var at = Math.Min(Math.Max(0, duration - .15), duration * (i + .5) / 8);
                var cache = Path.Combine(_paths.Root, "timeline-cache", _selected!.Clip.Id + "-" + i + ".jpg");
                var frame = await Task.Run(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                    if (!File.Exists(cache)) MediaTools.Thumbnail(source, cache, at);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 180; bitmap.UriSource = new Uri(cache); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
                });
                frames.Add(frame);
            }
            if (version == _selectionVersion && !_closing) { Timeline.Frames = frames; Timeline.InvalidateVisual(); }
            // Keep a bounded preview cache (about 40 clips). Original media is never touched.
            var cached = new DirectoryInfo(Path.Combine(_paths.Root, "timeline-cache")).EnumerateFiles("*.jpg").OrderByDescending(f => f.LastWriteTimeUtc).Skip(320);
            foreach (var file in cached) { try { file.Delete(); } catch (IOException) { } }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or NotSupportedException) { /* The poster remains available when previews cannot be generated. */ }
        finally { _filmstripLock.Release(); }
    }
    private void MediaEnded(object sender, RoutedEventArgs e) => SetPlaying(false);
    private void MediaFailed(object sender, ExceptionRoutedEventArgs e) { SetPlaying(false); ActionStatus.Text = "This video could not be played. Show in folder to open the saved file. " + e.ErrorException.Message; }
    private void SetPlaying(bool play)
    {
        _playing = play;
        if (Player == null) return;
        if (play) { PlayerPoster.Visibility = Visibility.Collapsed; Player.Play(); } else Player.Pause();
        PlayIcon.Symbol = play ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        PlayButton.ToolTip = play ? "Pause (Space)" : "Play (Space)";
    }
    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _duration <= 0) return;
        if (!_playing && (Player.Position.TotalSeconds >= Timeline.SelectionEnd - .1 || Player.Position.TotalSeconds < Timeline.SelectionStart)) SeekTo(Timeline.SelectionStart);
        SetPlaying(!_playing);
    }
    private void SeekTo(double seconds)
    {
        if (_selected == null || _duration <= 0) return;
        var at = Math.Clamp(seconds, 0, _duration); Player.Position = TimeSpan.FromSeconds(at); PlayerPoster.Visibility = Visibility.Collapsed;
        Timeline.Position = at; Timeline.InvalidateVisual(); _updating = true; Seek.Value = at; _updating = false;
        TimeLabel.Text = $"{Time(at)} / {Time(_duration)}";
    }
    private void PreviewMoment(double recordingTime)
    {
        if (_selected == null) return;
        SeekTo(PlaybackNavigation.BeforeEvent(recordingTime, _selected.Clip.RecordingOrigin, Timeline.SelectionStart, Timeline.SelectionEnd)); SetPlaying(true);
    }
    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating) SeekTo(e.NewValue); }
    private void SeekDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekUp(object sender, MouseButtonEventArgs e) { _seeking = false; SeekTo(Seek.Value); }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player == null || VolumeIcon == null) return;
        Player.Volume = e.NewValue; if (e.NewValue > 0) Player.IsMuted = false; UpdateVolumeIcon();
    }
    private void UpdateVolumeIcon() => VolumeIcon.Symbol = Player.IsMuted || Player.Volume <= 0 ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;
    private void MuteClick(object sender, RoutedEventArgs e)
    {
        if (Player.IsMuted || Player.Volume <= 0) { if (Player.Volume <= 0) VolumeSlider.Value = .7; Player.IsMuted = false; }
        else Player.IsMuted = true;
        UpdateVolumeIcon();
    }
    private void RangeInputChanged(object sender, RoutedEventArgs e)
    {
        if (_duration <= 0) return;
        if (!TryTime(StartInput.Text, out var start) || !TryTime(EndInput.Text, out var end) || start < 0 || end > _duration + .05 || end - start < .1) { ActionStatus.Text = "Use minutes:seconds. The range must be at least 0.1 seconds and inside the video."; UpdateRange(); return; }
        Timeline.SelectionStart = start; Timeline.SelectionEnd = Math.Min(end, _duration); UpdateRange(); SeekTo(start);
    }
    private void UpdateRange()
    {
        if (StartInput == null || EndInput == null) return;
        StartInput.Text = Time(Timeline.SelectionStart, true); EndInput.Text = Time(Timeline.SelectionEnd, true);
        RangeLabel.Text = _duration > 0 ? $"{Time(Timeline.SelectionEnd - Timeline.SelectionStart, true)} selected" : "Select a clip"; Timeline.InvalidateVisual();
    }
    private void ResetTrimClick(object sender, RoutedEventArgs e) { if (_selected == null) return; Timeline.SelectionStart = Math.Clamp(_selected.Clip.Start, 0, _duration); Timeline.SelectionEnd = Math.Clamp(_selected.Clip.End, Timeline.SelectionStart, _duration); UpdateRange(); SeekTo(Timeline.SelectionStart); }
    private void SetInClick(object sender, RoutedEventArgs e) { if (_duration <= 0) return; Timeline.SelectionStart = Math.Min(Player.Position.TotalSeconds, Math.Max(0, Timeline.SelectionEnd - .1)); UpdateRange(); }
    private void SetOutClick(object sender, RoutedEventArgs e) { if (_duration <= 0) return; Timeline.SelectionEnd = Math.Clamp(Player.Position.TotalSeconds, Math.Min(_duration, Timeline.SelectionStart + .1), _duration); UpdateRange(); }
    private static string Time(double seconds, bool precise = false) { var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)); return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}" + (precise ? $".{t.Milliseconds / 100}" : ""); }
    private static bool TryTime(string text, out double seconds) { seconds = 0; var parts = text.Trim().Split(':'); if (parts.Length > 3) return false; foreach (var part in parts) { if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0) return false; seconds = seconds * 60 + value; } return true; }

    private void FullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);
    private void VideoClicked(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) { SetFullscreen(!_fullscreen); e.Handled = true; } }
    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen && _selected == null) return;
        _fullscreen = fullscreen;
        if (fullscreen)
        {
            _priorWindowState = WindowState; _priorChrome = WindowChrome.GetWindowChrome(this); _priorResizeMode = ResizeMode;
            WindowState = WindowState.Normal; WindowChrome.SetWindowChrome(this, null); ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else { WindowState = WindowState.Normal; ResizeMode = _priorResizeMode; WindowChrome.SetWindowChrome(this, _priorChrome); WindowState = _priorWindowState; }
        foreach (var element in new UIElement[] { TitleBar, Sidebar, LibraryHeading, ClipBrowser, SelectedHeading, TrimWorkbench }) element.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        ExitFullscreenButton.Visibility = fullscreen ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = new GridLength(fullscreen ? 0 : 318); GutterColumn.Width = new GridLength(fullscreen ? 0 : 22);
        LibraryPanel.Margin = fullscreen ? new Thickness(0) : new Thickness(24, 20, 24, 16);
        Grid.SetRow(LibraryPanel, fullscreen ? 0 : 1); Grid.SetColumn(LibraryPanel, fullscreen ? 0 : 1); Grid.SetRowSpan(LibraryPanel, fullscreen ? 2 : 1); Grid.SetColumnSpan(LibraryPanel, fullscreen ? 2 : 1);
        ErrorBanner.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (_deleteCandidate != null) CancelDeleteClick(this, e); else if (_fullscreen) SetFullscreen(false); e.Handled = true; return; }
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox || _deleteCandidate != null || SettingsPanel.Visibility == Visibility.Visible) return;
        if (e.Key == Key.Space && e.OriginalSource is not Button) PlayClick(this, e);
        else if (e.Key == Key.F) SetFullscreen(!_fullscreen);
        else if (e.Key == Key.M) MuteClick(this, e);
        else if (e.Key == Key.I) SetInClick(this, e);
        else if (e.Key == Key.O) SetOutClick(this, e);
        else if (e.Key == Key.Delete) DeleteClick(this, e);
        else if (e.Key is Key.Left or Key.Right && e.OriginalSource is not Controls.ClipTimeline && e.OriginalSource is not Slider) SeekTo(Player.Position.TotalSeconds + (e.Key == Key.Right ? 5 : -5));
        else return;
        e.Handled = true;
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _exporting) return;
        SetPlaying(false); _deleteCandidate = _selected;
        bool full = _matches;
        DeleteTitle.Text = full ? "Delete this match?" : "Delete this clip?";
        DeleteDescription.Text = full ? "The match video will move to the Windows Recycle Bin. Highlights that reference this match will also be removed from the library." : _selected.Clip.IsVirtual ? "This highlight will be removed from the library. Your full match recording will be kept." : "The video will move to the Windows Recycle Bin. You can restore the file from there.";
        ConfirmDeleteButton.Content = full ? "Delete match" : "Delete clip"; DeleteOverlay.Visibility = Visibility.Visible; CancelDeleteButton.Focus();
        LibraryPanel.IsEnabled = false; Sidebar.IsEnabled = false;
    }
    private void CancelDeleteClick(object sender, RoutedEventArgs e) { if (_deleting) return; DeleteOverlay.Visibility = Visibility.Collapsed; _deleteCandidate = null; LibraryPanel.IsEnabled = true; Sidebar.IsEnabled = true; DeleteButton.Focus(); }
    private async void ConfirmDeleteClick(object sender, RoutedEventArgs e)
    {
        var item = _deleteCandidate; if (item == null) return;
        _deleting = true; ConfirmDeleteButton.IsEnabled = false; CancelDeleteButton.IsEnabled = false;
        try
        {
            Player.Close(); _selectionVersion++;
            var full = _matches;
            await _filmstripLock.WaitAsync();
            bool recycle;
            try
            {
                recycle = await Task.Run(() => LibraryDeletion.Remove(_settings.OutputDirectory, item.Session.Id, item.Clip.Id, full,
                    media => RecycleFile.DeleteFile(media, RecycleUI.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, RecycleCancel.ThrowException)));
            }
            finally { _filmstripLock.Release(); }
            _deleting = false; CancelDeleteClick(sender, e); ClearPlayer(); _catalogSignature = ""; RefreshCatalog(true); ActionStatus.Text = full ? "Match moved to the Recycle Bin." : recycle ? "Clip moved to the Recycle Bin." : "Highlight removed. The full match is kept.";
        }
        catch (Exception ex) { DeleteDescription.Text = "Could not delete: " + ex.Message; }
        finally { _deleting = false; ConfirmDeleteButton.IsEnabled = true; CancelDeleteButton.IsEnabled = true; }
    }
    private void RevealClick(object sender, RoutedEventArgs e) { if (_selected == null) return; var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; info.ArgumentList.Add("/select," + _selected.Clip.Path); Process.Start(info); }
    private async Task EnsureRecorder(bool resumeOnOpen = false)
    {
        if (_reconnecting || _closing) return;
        _reconnecting = true;
        try
        {
            if (await Command("status")) { if (resumeOnOpen && !_status.Armed && _status.Error == null) await Command("arm"); return; }
            var file = Path.Combine(AppContext.BaseDirectory, "Echo.Recorder.exe");
            if (!File.Exists(file)) { ErrorBanner.Text = "Recorder is missing. Run scripts/build.ps1 to assemble Echo."; return; }
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(_paths.Root); Process.Start(info)?.Dispose();
            await Task.Delay(1000); RefreshStatus();
        }
        catch (Exception e) { ErrorBanner.Text = "Recorder could not start: " + e.Message; }
        finally { _reconnecting = false; }
    }
    private async Task<bool> Command(string command)
    {
        try
        {
            using var timeout = new CancellationTokenSource(1200);
            await using var client = new NamedPipeClientStream(".", _paths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await client.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
            using var reader = new StreamReader(client, leaveOpen: true); var reply = await reader.ReadLineAsync(timeout.Token);
            var status = JsonSerializer.Deserialize<RecorderStatus>(reply ?? "null", JsonStore.Options); if (status != null) _status = status; return status != null;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { return false; }
    }
    private async void CaptureClick(object sender, RoutedEventArgs e)
    {
        if (!await Command(_status.Error != null ? "retry" : _status.Armed ? "disarm" : "arm")) await EnsureRecorder();
    }
    private async void SaveMomentClick(object sender, RoutedEventArgs e) { ActionStatus.Text = await Command("save") && _status.Recording ? "Manual moment requested." : "Your manual moments can be saved while a match is recording."; }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void SetFilter() { foreach (var button in new[] { FilterAll, FilterKills, FilterAssists, FilterDeaths }) { var active = (string)button.Tag == _filter; button.Background = (Brush)FindResource(active ? "AccentBrush" : "PanelBrush"); button.Foreground = active ? new SolidColorBrush(Color.FromRgb(16, 39, 30)) : (Brush)FindResource("SecondaryBrush"); } }
    private void FilterClick(object sender, RoutedEventArgs e) { _filter = (string)((Button)sender).Tag; SetFilter(); ApplyFilter(); }
    private void SetNav(string active) { foreach (var pair in new[] { (LibraryNav, "Clips"), (MatchesNav, "Matches"), (SettingsNav, "Settings") }) { pair.Item1.Background = pair.Item2 == active ? new SolidColorBrush(Color.FromRgb(37, 60, 51)) : Brushes.Transparent; pair.Item1.Foreground = (Brush)FindResource(pair.Item2 == active ? "AccentBrush" : "SecondaryBrush"); } }
    private void LibraryClick(object sender, RoutedEventArgs e) { _matches = false; _filter = "All"; PageTitle.Text = "Your moments"; SettingsPanel.Visibility = Visibility.Collapsed; LibraryPanel.Visibility = Visibility.Visible; SetNav("Clips"); SetFilter(); RefreshCatalog(true); }
    private void MatchesClick(object sender, RoutedEventArgs e) { _matches = true; _filter = "All"; PageTitle.Text = "Your matches"; SettingsPanel.Visibility = Visibility.Collapsed; LibraryPanel.Visibility = Visibility.Visible; SetNav("Matches"); SetFilter(); RefreshCatalog(true); }
    private void SettingsClick(object sender, RoutedEventArgs e) { SetPlaying(false); LibraryPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible; SetNav("Settings"); LoadSettingsFields(); }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject source) { while (source != null && source != TitleBar) { if (source is Button) return; source = VisualTreeHelper.GetParent(source); } } if (e.ClickCount == 2) MaximizeClick(sender, e); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) { if (_fullscreen) SetFullscreen(false); else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _exporting || CaptureActive()) return;
        var dialog = new SaveFileDialog { Title = "Export clip", Filter = "MP4 video|*.mp4", FileName = $"Echo-{_selected.Session.StartedAt:yyyyMMdd-HHmm}-{_selected.Clip.Id[..6]}.mp4", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) { ActionStatus.Text = "Choose a new filename; Echo keeps existing videos intact."; return; }
        var input = _selected.Clip.Path; var start = Timeline.SelectionStart; var length = Timeline.SelectionEnd - start;
        var nativeEncoder = _selected.Session.Encoder ?? _status.Encoder ?? "nvenc";
        string[] videoArgs = nativeEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase)
            ? ["-c:v", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "20", "-qp_p", "20"]
            : nativeEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase)
            ? ["-c:v", "h264_qsv", "-preset", "fast", "-global_quality", "20"]
            : ["-c:v", "h264_nvenc", "-preset", "p3", "-cq", "20"];
        if (length < .1) return;
        _exporting = true; ExportButton.IsEnabled = false; ActionStatus.Text = "Exporting your clip…";
        var temp = dialog.FileName + ".echo-partial";
        try
        {
            await Task.Run(async () =>
            {
                var info = new ProcessStartInfo(MediaTools.Find("ffmpeg")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-hide_banner", "-v", "error", "-n", "-ss", start.ToString("0.###", CultureInfo.InvariantCulture), "-i", input, "-t", length.ToString("0.###", CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a?" }.Concat(videoArgs).Concat(["-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", temp])) info.ArgumentList.Add(arg);
                using var p = Process.Start(info) ?? throw new InvalidOperationException("FFmpeg could not start.");
                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                var errors = p.StandardError.ReadToEndAsync(); var watch = Stopwatch.StartNew();
                while (!p.HasExited)
                {
                    if (_closing) { p.Kill(true); throw new OperationCanceledException("Export stopped because Echo closed."); }
                    if (CaptureActive()) { p.Kill(true); throw new OperationCanceledException("Export stopped because a recording started. Try again after the match."); }
                    if (watch.Elapsed.TotalMinutes > 15) { p.Kill(true); throw new TimeoutException("Export took too long."); }
                    await Task.Delay(250);
                }
                if (p.ExitCode != 0) throw new InvalidOperationException(await errors);
                _ = MediaTools.Duration(temp); File.Move(temp, dialog.FileName);
            });
            ActionStatus.Text = "Exported: " + dialog.FileName;
        }
        catch (Exception ex) { ActionStatus.Text = "Export could not finish: " + ex.Message[..Math.Min(400, ex.Message.Length)]; }
        finally { _exporting = false; if (File.Exists(temp)) File.Delete(temp); RefreshStatus(); }
    }


    private void LoadSettingsFields()
    {
        ModeBox.SelectedIndex = _settings.Mode == RecordingMode.Clips ? 0 : 1; GameAudioBox.IsChecked = _settings.GameAudio; DesktopAudioBox.IsChecked = _settings.DesktopAudio; MicBox.IsChecked = _settings.Microphone;
        CaptureMethodBox.SelectedIndex = _settings.CaptureMethod == CaptureMethod.Window ? 0 : 1;
        KillsBox.IsChecked = _settings.CaptureKills; AssistsBox.IsChecked = _settings.CaptureAssists; DeathsBox.IsChecked = _settings.CaptureDeaths;
        BeforeInput.Text = _settings.BeforeSeconds.ToString(CultureInfo.InvariantCulture); AfterInput.Text = _settings.AfterSeconds.ToString(CultureInfo.InvariantCulture);
        ResolutionBox.SelectedIndex = _settings.Height == 720 ? 0 : 1; FpsBox.SelectedIndex = _settings.Fps == 30 ? 0 : 1; OutputInput.Text = _settings.OutputDirectory;
    }
    private void BrowseClick(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Choose recording folder" }; if (dialog.ShowDialog(this) == true) OutputInput.Text = dialog.FolderName; }
    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var before = int.Parse(BeforeInput.Text, CultureInfo.InvariantCulture); var after = int.Parse(AfterInput.Text, CultureInfo.InvariantCulture);
            var buffer = Math.Max(40, before + after + 5);
            var settings = _settings with { Mode = ModeBox.SelectedIndex == 0 ? RecordingMode.Clips : RecordingMode.FullMatch, CaptureMethod = CaptureMethodBox.SelectedIndex == 0 ? CaptureMethod.Window : CaptureMethod.FullscreenDisplay, GameAudio = GameAudioBox.IsChecked == true,
                DesktopAudio = DesktopAudioBox.IsChecked == true, Microphone = MicBox.IsChecked == true, CaptureKills = KillsBox.IsChecked == true, CaptureAssists = AssistsBox.IsChecked == true,
                CaptureDeaths = DeathsBox.IsChecked == true, BeforeSeconds = before, AfterSeconds = after, BufferSeconds = buffer,
                BufferMegabytes = Math.Max(128, (int)Math.Ceiling(_settings.BitrateKbps * 1000d / 8 * buffer * 1.3 / 1048576)),
                Width = ResolutionBox.SelectedIndex == 0 ? 1280 : 1920, Height = ResolutionBox.SelectedIndex == 0 ? 720 : 1080, Fps = FpsBox.SelectedIndex == 0 ? 30 : 60, OutputDirectory = OutputInput.Text.Trim() };
            settings.Validate(); Directory.CreateDirectory(settings.OutputDirectory); JsonStore.Write(_paths.Settings, settings); _settings = settings;
            SettingsError.Text = ""; SettingsMessage.Text = "Saved. These settings apply to the next recording."; _catalogSignature = "";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException) { SettingsError.Text = ex.Message; SettingsMessage.Text = "Settings were not saved."; }
    }


}

internal sealed class ClipItem
{
    public Session Session { get; }
    public MediaClip Clip { get; }
    public string Title => Session.Champion;
    public string Label => Clip.Label;
    public string ShortDate => Session.StartedAt.ToLocalTime().ToString("MMM d · HH:mm", CultureInfo.InvariantCulture);
    public string DateLabel => Session.StartedAt.ToLocalTime().ToString("MMM d, yyyy · HH:mm", CultureInfo.InvariantCulture);
    public string DurationLabel => TimeSpan.FromSeconds(Math.Max(0, Clip.End - Clip.Start)).ToString(@"mm\:ss");
    public BitmapSource? Thumbnail { get; }
    public ClipItem(Session session, MediaClip clip, string? thumbnail)
    {
        Session = session; Clip = clip;
        if (thumbnail == null || !File.Exists(thumbnail)) return;
        try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 420; bitmap.UriSource = new Uri(thumbnail); bitmap.EndInit(); bitmap.Freeze(); Thumbnail = bitmap; }
        catch (Exception e) when (e is IOException or NotSupportedException) { }
    }
}
