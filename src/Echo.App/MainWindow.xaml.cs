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
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<ClipItem> _items = [];
    private ClipItem? _selected, _deleteCandidate;
    private string _filter = "All", _catalogSignature = "";
    private bool _updating, _seeking, _playing, _matches, _exporting, _fullscreen, _reconnecting, _deleting;
    private volatile bool _closing;
    private double _duration;
    private int _ticks, _selectionVersion, _galleryPage;
    private bool _bindingGallery, _trimOpen, _settingsDirty;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private const int GalleryPageSize = 32;
    private string _settingsCategory = "Recording";
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
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); SaveSettingsClick(this, new()); };
        SettingsPanel.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) AutoSave(false); }));
        SettingsPanel.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => AutoSave(false)));
        SettingsPanel.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => AutoSave(false)));
        SettingsPanel.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource != OutputInput) AutoSave(true); }));
        OutputInput.LostKeyboardFocus += (_, _) => AutoSave(false);
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        Player.Volume = VolumeSlider.Value;
        Timeline.SeekRequested += SeekTo;
        Timeline.MomentRequested += t => { SeekTo(Math.Max(Timeline.SelectionStart, t - 3)); SetPlaying(true); };
        Timeline.RangeChanged += (_, _) => UpdateRange();
        PlaybackActivity.SeekRequested += SeekTo;
        PlaybackActivity.MomentRequested += t=>{SeekTo(Math.Max(PlaybackActivity.Start,t-3));SetPlaying(true);};
        InputPlacementPreview.PlacementChanged += preset => { _inputPreset=preset; PersistInputPreset(); };
        SettingsScroll.SizeChanged+=(_,_)=>InputPlacementPreview.MaxWidth=Math.Clamp((SettingsScroll.ActualHeight-340)*16/9,320,880);
        Loaded += async (_, _) => { if (_startRecorder) await EnsureRecorder(true); RefreshCatalog(true); RefreshStatus(); };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        Closing += (_, _) => { _closing = true; _selectionVersion++; _timer.Stop(); _toastTimer.Stop(); _settingsTimer.Stop(); if (_settingsDirty) SaveSettingsClick(this, new()); Player.Close(); };
        _overlayReady=true;
    }
    private void Tick()
    {
        if (App.IsExiting) return;
        if (++_ticks % 5 == 0) RefreshStatus();
        if (_ticks % 25 == 0)
        {
            RefreshCatalog();
            if (_startRecorder && DateTimeOffset.UtcNow - _status.UpdatedAt > TimeSpan.FromSeconds(8) && !_reconnecting) _ = EnsureRecorder();
        }
        if (EditorPanel.Visibility != Visibility.Visible || _selected == null || _duration <= 0 || _seeking) return;
        var position = Math.Clamp(Player.Position.TotalSeconds, 0, _duration);
        _updating = true; Seek.Value = position; _updating = false;
        if (Math.Abs(Timeline.Position - position) > .01) { Timeline.Position = position; Timeline.InvalidateVisual(); }
        PlaybackActivity.Position=position;PlaybackActivity.InvalidateVisual();
        UpdatePlaybackTime(position);
        UpdatePlaybackInputs(position);
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
            HeaderStatus.Text = _status.Recording ? (_status.ActiveMode == RecordingMode.FullMatch ? "Full match " : "Highlights ") + Time(_status.ElapsedSeconds) : !fresh ? "Capture offline" : _status.Error != null ? "Capture needs attention" : _status.Armed ? "Auto capture on" : "Capture paused";
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_status.Recording ? "#F1B7BD" : fresh && _status.Armed ? "#C2D4FF" : "#A1A7B2"));
            CaptureButton.ToolTip = !fresh ? "Reconnect capture" : _status.Error != null ? "Retry capture: " + _status.Error : _status.Armed ? "Automatic capture is on. Click to pause." : "Resume automatic capture";
            CaptureGlyph.Kind = _status.Armed ? "Recording" : "Play";
            RecorderLabel.Text = !fresh ? "Offline" : _status.Armed ? "Auto on" : "Paused";
            ErrorBanner.Text = _status.Error ?? "";
            ShortcutMessage.Text = _status.ShortcutError ?? (_status.ManualShortcut == _settings.ManualShortcut && _settings.ManualShortcut.Length > 0 ? "Active" : "");
            ExportButton.IsEnabled = _selected != null && !CaptureActive() && !_exporting && _duration > 0;
            QuickExportButton.IsEnabled=ExportButton.IsEnabled;
            ExportButton.ToolTip = CaptureActive() ? "Export is available after the match." : "Save the selected range as a new MP4";
            ExportHint.Text = CaptureActive() ? "Finish your match to export clips." : "Save your selection as a new MP4.";
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
                if (session == null) continue;
                if (_matches)
                {
                    if (session.Mode == RecordingMode.FullMatch && session.RecordingPath is { } path && (File.Exists(path) || session.EndedAt == null)) _items.Add(new(session, new(session.Id, path, 0, session.Duration, "Full match", true), FindMatchPoster(file.DirectoryName!, session)));
                }
                else if (session.EndedAt != null) foreach (var clip in session.Clips) if (!clip.VideoUnavailable && File.Exists(clip.Path)) _items.Add(new(session, clip, clip.ThumbnailPath ?? Path.Combine(file.DirectoryName!, clip.Id + ".jpg")));
            }
            _items.Sort((a, b) => b.Session.StartedAt.CompareTo(a.Session.StartedAt)); ApplyFilter();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorBanner.Text = "Could not open the recording folder: " + e.Message; }
    }
    private static string? FindMatchPoster(string folder, Session session) => new[] { Path.Combine(folder, "match.jpg") }.Concat(session.Clips.Select(c => c.ThumbnailPath ?? Path.Combine(folder, c.Id + ".jpg"))).FirstOrDefault(File.Exists);
    private void ApplyFilter(bool resetPage = false)
    {
        if (ClipList == null || SearchBox == null || SortBox == null) return;
        if (resetPage) _galleryPage = 0;
        var query = SearchBox.Text.Trim();
        var filtered = _items.Where(i => (_filter == "All" || i.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || (i.Title + " " + i.DateLabel + " " + i.Label).Contains(query, StringComparison.OrdinalIgnoreCase)));
        var visible = (SortBox.SelectedIndex == 1 ? filtered.OrderBy(i => i.Session.StartedAt).ThenBy(i => i.Clip.RecordingOrigin + i.Clip.Start) : filtered.OrderByDescending(i => i.Session.StartedAt).ThenByDescending(i => i.Clip.RecordingOrigin + i.Clip.Start)).ToArray();
        var pages = Math.Max(1, (int)Math.Ceiling(visible.Length / (double)GalleryPageSize));
        _galleryPage = Math.Clamp(_galleryPage, 0, pages - 1);
        _bindingGallery = true;
        ClipList.ItemsSource = visible.Skip(_galleryPage * GalleryPageSize).Take(GalleryPageSize).ToArray();
        ClipList.SelectedItem = null;
        _bindingGallery = false;
        LibraryCount.Text = $"{visible.Length} {(_matches ? "matches" : "moments")} ";
        EmptyGallery.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyList.Text = _items.Count == 0 ? (_matches ? (_settings.Mode == RecordingMode.FullMatch ? "Your next full match will appear here." : "Choose Entire match in Recording settings to save complete games here.") : "Keep Echo running and play League. Your kills, assists and deaths will appear here automatically.") : "No clips match your search. Try another event or search term.";
        PageLabel.Text = $"Page {_galleryPage + 1} of {pages}"; PreviousPageButton.IsEnabled = _galleryPage > 0; NextPageButton.IsEnabled = _galleryPage < pages - 1;
        if (visible.Length == 0 && _selected == null) ClearPlayer();
    }
    private void GalleryWidthChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(400, ClipList.ActualWidth - 22); var columns = Math.Max(2, Math.Floor(width / 285));
        var tile = Math.Floor(width / columns) - 16;
        ClipList.Resources["GalleryTileWidth"] = tile; ClipList.Resources["GalleryThumbnailHeight"] = (tile - 12) * 9 / 16;
    }
    private void SortChanged(object sender, SelectionChangedEventArgs e) { if (IsInitialized) ApplyFilter(true); }
    private void PreviousPageClick(object sender, RoutedEventArgs e) { _galleryPage--; ApplyFilter(); }
    private void NextPageClick(object sender, RoutedEventArgs e) { _galleryPage++; ApplyFilter(); }
    private void OpenEditor()
    {
        LibraryPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Collapsed; EditorPanel.Visibility = Visibility.Visible; SetTrimOpen(false);
    }
    private void BackToLibraryClick(object sender, RoutedEventArgs e)
    {
        if (_fullscreen) SetFullscreen(false);
        SetPlaying(false); EditorPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Collapsed; LibraryPanel.Visibility = Visibility.Visible;
        _bindingGallery = true; ClipList.SelectedItem = null; _bindingGallery = false;
    }
    private void JumpBackClick(object sender, RoutedEventArgs e) => SeekTo(Player.Position.TotalSeconds - 5);
    private void JumpForwardClick(object sender, RoutedEventArgs e) => SeekTo(Player.Position.TotalSeconds + 5);
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
        if (_bindingGallery || ClipList.SelectedItem is not ClipItem item) return;
        if (!item.IsReady) { ShowToast(item.StateLabel == "Recording" ? "This match is recording. It will be ready when the match ends." : "This recording is being prepared."); return; }
        OpenEditor();
        if (_selected?.Clip.Id == item.Clip.Id && _selected.Clip.Path == item.Clip.Path) { _selected = item; return; }
        _selectionVersion++; _selected = item; _duration = 0; SetPlaying(false); Player.Close(); EmptyPlayer.Visibility = Visibility.Collapsed;
        LoadInputData(item);
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
        Timeline.Moments = _selected.Session.Moments.Select(m => (Time: PlaybackNavigation.FileTime(_selected.Clip, m.RecordingTime), m.Kind)).Where(m => m.Time.HasValue).Select(m => (m.Time!.Value, m.Kind)).ToArray();
        _updating = false; UpdateRange();
        var first = _selected.Session.Moments.FirstOrDefault(m => PlaybackNavigation.FileTime(_selected.Clip, m.RecordingTime) is { } t && t >= Timeline.SelectionStart && t <= Timeline.SelectionEnd);
        SeekTo(first == null ? Timeline.SelectionStart : PlaybackNavigation.BeforeEvent(_selected.Clip, first.RecordingTime, Timeline.SelectionStart, Timeline.SelectionEnd));
        Player.Pause(); PlayerPoster.Visibility = PlayerPoster.Source != null ? Visibility.Visible : Visibility.Collapsed;
        RefreshStatus(); _ = LoadFilmstrip(_selected.Clip.Path, _duration, _selectionVersion);
    }
    private async Task LoadFilmstrip(string source, double duration, int version)
    {
        // Decode at most eight small frames, serially, only outside active capture. No background library-wide scan.
        if (CaptureActive()) return;
        await _filmstripLock.WaitAsync();
        try
        {
            if (_closing || version != _selectionVersion || CaptureActive()) return;
            var posterAt = Timeline.Position;
            var mediaKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..12];
            var posterPath = Path.Combine(_paths.Root, "timeline-cache", $"poster-{mediaKey}-{posterAt:0}.jpg");
            var poster = await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(posterPath)!);
                MediaTools.Thumbnail(source, posterPath, posterAt, 1280);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1280; bitmap.UriSource = new Uri(posterPath); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            });
            if (version != _selectionVersion || _closing) return;
            if (!_playing && Math.Abs(Player.Position.TotalSeconds - posterAt) < .15) { PlayerPoster.Source = poster; PlayerPoster.Visibility = Visibility.Visible; }
            var frames = new List<ImageSource>();
            for (int i = 0; i < 8; i++)
            {
                if (_closing || version != _selectionVersion || CaptureActive()) return;
                var at = Math.Min(Math.Max(0, duration - .15), duration * (i + .5) / 8);
                var cache = Path.Combine(_paths.Root, "timeline-cache", mediaKey + "-" + i + ".jpg");
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
        PlayIcon.Kind = play ? "Pause" : "Play";
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
        PlaybackActivity.Position=at;PlaybackActivity.InvalidateVisual();
        UpdatePlaybackTime(at);
        UpdatePlaybackInputs(at);
    }
    private void PreviewMoment(double recordingTime)
    {
        if (_selected == null) return;
        SeekTo(PlaybackNavigation.BeforeEvent(_selected.Clip, recordingTime, Timeline.SelectionStart, Timeline.SelectionEnd)); SetPlaying(true);
    }
    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating) SeekTo(e.NewValue); }
    private void SeekDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekUp(object sender, MouseButtonEventArgs e) { _seeking = false; SeekTo(Seek.Value); }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player == null || VolumeIcon == null) return;
        Player.Volume = e.NewValue; if (e.NewValue > 0) Player.IsMuted = false; UpdateVolumeIcon();
    }
    private void UpdateVolumeIcon() => VolumeIcon.Kind = Player.IsMuted || Player.Volume <= 0 ? "VolumeMuted" : "Volume";
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
        _updating = true;
        StartInput.Text = Time(Timeline.SelectionStart, true); EndInput.Text = Time(Timeline.SelectionEnd, true);
        RangeLabel.Text = _duration > 0 ? Time(Timeline.SelectionEnd - Timeline.SelectionStart, true) : "00:00.0"; Timeline.InvalidateVisual(); _updating = false;
        UpdateSeekBounds();
        RefreshPlaybackActivity();
    }
    private void ResetTrimClick(object sender, RoutedEventArgs e) { if (_selected == null) return; Timeline.SelectionStart = Math.Clamp(_selected.Clip.Start, 0, _duration); Timeline.SelectionEnd = Math.Clamp(_selected.Clip.End, Timeline.SelectionStart, _duration); UpdateRange(); SeekTo(Timeline.SelectionStart); }
    private void SetInClick(object sender, RoutedEventArgs e) { if (_duration <= 0) return; Timeline.SelectionStart = Math.Min(Player.Position.TotalSeconds, Math.Max(0, Timeline.SelectionEnd - .1)); UpdateRange(); }
    private void SetOutClick(object sender, RoutedEventArgs e) { if (_duration <= 0) return; Timeline.SelectionEnd = Math.Clamp(Player.Position.TotalSeconds, Math.Min(_duration, Timeline.SelectionStart + .1), _duration); UpdateRange(); }
    private static string Time(double seconds, bool precise = false) { var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)); return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}" + (precise ? $".{t.Milliseconds / 100}" : ""); }
    private static bool TryTime(string text, out double seconds) { seconds = 0; var parts = text.Trim().Split(':'); if (parts.Length > 3) return false; foreach (var part in parts) { if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0) return false; seconds = seconds * 60 + value; } return true; }

    private void ToggleTrimClick(object sender, RoutedEventArgs e) => SetTrimOpen(!_trimOpen);
    private void SetTrimOpen(bool open)
    {
        _trimOpen = open; Inspector.Visibility = TrimWorkbench.Visibility = open && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = new GridLength(open && !_fullscreen ? 250 : 0);
        EditorPanel.RowDefinitions[2].Height = new GridLength(open && !_fullscreen ? 216 : 0);
        TrimButtonLabel.Text = open ? "Done" : "Trim";
        UpdateActivityVisibility();
        UpdateSeekBounds();
    }
    private void UpdateSeekBounds()
    {
        if (_duration <= 0) return;
        _updating = true; Seek.Minimum = _trimOpen ? 0 : Timeline.SelectionStart; Seek.Maximum = _trimOpen ? _duration : Math.Max(Seek.Minimum, Timeline.SelectionEnd); _updating = false;
    }
    private void UpdatePlaybackTime(double position)
    {
        var origin = _trimOpen ? 0 : Timeline.SelectionStart;
        var length = _trimOpen ? _duration : Timeline.SelectionEnd - Timeline.SelectionStart;
        TimeLabel.Text = $"{Time(position - origin)} / {Time(length)}";
    }
    private void AutoSave(bool debounce)
    {
        if (_updating || _closing) return;
        _settingsDirty = true; _settingsTimer.Stop();
        if (debounce) { _settingsTimer.Start(); }
        else SaveSettingsClick(this, new());
    }
    private void ShortcutKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        if (key is Key.Delete or Key.Back) { ShortcutInput.Text = ""; AutoSave(false); return; }
        var mods = Keyboard.Modifiers; var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl"); if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt"); if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        var vk = KeyInterop.VirtualKeyFromKey(key); parts.Add(vk is >= 0x30 and <= 0x5A ? ((char)vk).ToString() : key.ToString());
        try { ShortcutInput.Text = ShortcutGesture.Parse(string.Join('+', parts))?.Text ?? ""; AutoSave(false); }
        catch (ArgumentException ex) { ShortcutMessage.Text = ex.Message; }
    }
    private void ClearShortcutClick(object sender, RoutedEventArgs e) { ShortcutInput.Text = ""; AutoSave(false); }
    private void FullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);
    private void VideoClicked(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) { SetFullscreen(!_fullscreen); e.Handled = true; } }
    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen && _selected == null) return;
        _fullscreen = fullscreen;
        FullscreenToggleIcon.Kind=fullscreen?"ExitFullscreen":"Fullscreen";
        FullscreenToggleButton.ToolTip=fullscreen?"Exit full screen (Esc)":"Full screen (F)";
        if (fullscreen)
        {
            _priorWindowState = WindowState; _priorChrome = WindowChrome.GetWindowChrome(this); _priorResizeMode = ResizeMode;
            WindowState = WindowState.Normal; WindowChrome.SetWindowChrome(this, null); ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else { WindowState = WindowState.Normal; ResizeMode = _priorResizeMode; WindowChrome.SetWindowChrome(this, _priorChrome); WindowState = _priorWindowState; }
        foreach (var element in new UIElement[] { TitleBar, Sidebar, SelectedHeading }) element.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        InspectorColumn.Width = new GridLength(!fullscreen && _trimOpen ? 250 : 0);
        Inspector.Visibility = TrimWorkbench.Visibility = !fullscreen && _trimOpen ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.RowDefinitions[0].Height = new GridLength(fullscreen ? 0 : 70);
        EditorPanel.RowDefinitions[2].Height = new GridLength(!fullscreen && _trimOpen ? 216 : 0);
        VideoSurface.Margin = fullscreen ? new Thickness(0) : new Thickness(24, 20, 24, 0);
        Grid.SetRow(EditorPanel, fullscreen ? 0 : 2); Grid.SetColumn(EditorPanel, fullscreen ? 0 : 1); Grid.SetRowSpan(EditorPanel, fullscreen ? 3 : 1); Grid.SetColumnSpan(EditorPanel, fullscreen ? 2 : 1);
        UpdateActivityVisibility();
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement == ShortcutInput) return;
        if (e.Key == Key.Escape)
        {
            if (_deleteCandidate != null) CancelDeleteClick(this, e);
            else if (_fullscreen) SetFullscreen(false);
            else if (_trimOpen) SetTrimOpen(false);
            else if (EditorPanel.Visibility == Visibility.Visible) BackToLibraryClick(this, e);
            e.Handled = true; return;
        }
        if (EditorPanel.Visibility != Visibility.Visible || _deleteCandidate != null) return;
        for (var node = Keyboard.FocusedElement as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.TextBoxBase or ComboBox) return;
        if (e.Key == Key.Space && e.OriginalSource is not Button) PlayClick(this, e);
        else if (e.Key == Key.F) SetFullscreen(!_fullscreen);
        else if (e.Key == Key.M) MuteClick(this, e);
        else if (e.Key == Key.I) { SetTrimOpen(true); SetInClick(this, e); }
        else if (e.Key == Key.O) { SetTrimOpen(true); SetOutClick(this, e); }
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
        EditorPanel.IsEnabled = false; Sidebar.IsEnabled = false;
    }
    private void CancelDeleteClick(object sender, RoutedEventArgs e) { if (_deleting) return; DeleteOverlay.Visibility = Visibility.Collapsed; _deleteCandidate = null; EditorPanel.IsEnabled = true; Sidebar.IsEnabled = true; DeleteButton.Focus(); }
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
            _deleting = false; CancelDeleteClick(sender, e); ClearPlayer(); BackToLibraryClick(sender, e); _catalogSignature = ""; RefreshCatalog(true); ShowToast(full ? "Match moved to the Recycle Bin." : recycle ? "Clip moved to the Recycle Bin." : "Highlight removed. The full match is kept.");
        }
        catch (Exception ex) { DeleteDescription.Text = "Could not delete: " + ex.Message; }
        finally { _deleting = false; ConfirmDeleteButton.IsEnabled = true; CancelDeleteButton.IsEnabled = true; }
    }
    private void RevealClick(object sender, RoutedEventArgs e) { if (_selected == null) return; try { FolderReveal.Show(_selected.Clip.Path); } catch (Exception ex) { ShowToast("Could not open folder: " + ex.Message); } }
    private async Task EnsureRecorder(bool resumeOnOpen = false)
    {
        if (_reconnecting || _closing || App.IsExiting) return;
        _reconnecting = true;
        try
        {
            if (await Command("status")) { if (resumeOnOpen && !_status.Armed && _status.Error == null) await Command("arm"); return; }
            if (App.IsExiting) return;
            var file = Path.Combine(AppContext.BaseDirectory, "Echo.Recorder.exe");
            if (!File.Exists(file)) { ErrorBanner.Text = "Recorder is missing. Run scripts/build.ps1 to assemble Echo."; return; }
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(_paths.Root); if(App.IsShutdownProbe)info.ArgumentList.Add("--paused"); Process.Start(info)?.Dispose();
            await Task.Delay(1000); RefreshStatus();
        }
        catch (Exception e) { ErrorBanner.Text = "Recorder could not start: " + e.Message; }
        finally { _reconnecting = false; }
    }
    internal Task StartShutdownProbe() => EnsureRecorder(false);
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
    private void ShowToast(string message)
    {
        ToastText.Text = message; Toast.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start();
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(ToastText)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void DismissToastClick(object sender, RoutedEventArgs e) { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); }
    private async void SaveMomentClick(object sender, RoutedEventArgs e)
        => ShowToast(await Command("save") && _status.Recording ? "Moment requested. Echo will keep the action and its ending." : "Manual moments are available while League is recording.");
    private void AudioRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockPanel row || FindName((string)row.Tag) is not CheckBox control) return;
        for (var node = e.OriginalSource as DependencyObject; node != null && node != row; node = VisualTreeHelper.GetParent(node)) if (node == control) return;
        control.IsChecked = control.IsChecked != true; e.Handled = true;
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter(true);
    private void SetFilter()
    {
        foreach (var button in new[] { FilterAll, FilterKills, FilterAssists, FilterDeaths })
        {
            var active = (string)button.Tag == _filter;
            button.Background = active ? new SolidColorBrush(Color.FromRgb(194, 212, 255)) : Brushes.Transparent;
            button.Foreground = active ? new SolidColorBrush(Color.FromRgb(21, 32, 59)) : (Brush)FindResource("SecondaryBrush");
            button.BorderBrush = active ? Brushes.Transparent : (Brush)FindResource("LineBrush");
        }
    }
    private void FilterClick(object sender, RoutedEventArgs e) { _filter = (string)((Button)sender).Tag; SetFilter(); ApplyFilter(true); }
    private void SetNav(string active)
    {
        foreach (var pair in new[] { (LibraryNav, "Clips"), (MatchesNav, "Matches"), (SettingsNav, "Settings") })
        {
            pair.Item1.Background = pair.Item2 == active ? new SolidColorBrush(Color.FromRgb(41, 48, 64)) : Brushes.Transparent;
            pair.Item1.Foreground = (Brush)FindResource(pair.Item2 == active ? "AccentBrush" : "SecondaryBrush");
        }
    }
    private void LibraryClick(object sender, RoutedEventArgs e) { _matches = false; _filter = "All"; _galleryPage = 0; PageTitle.Text = "Library"; BackToLibraryClick(sender, e); SetNav("Clips"); SetFilter(); RefreshCatalog(true); }
    private void MatchesClick(object sender, RoutedEventArgs e) { _matches = true; _filter = "All"; _galleryPage = 0; PageTitle.Text = "Matches"; BackToLibraryClick(sender, e); SetNav("Matches"); SetFilter(); RefreshCatalog(true); }
    private void SettingsClick(object sender, RoutedEventArgs e) { if (_fullscreen) SetFullscreen(false); SetPlaying(false); LibraryPanel.Visibility = Visibility.Collapsed; EditorPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible; SetNav("Settings"); ShowSettingsCategory(_settingsCategory); }
    private void SettingsCategoryClick(object sender, RoutedEventArgs e) => ShowSettingsCategory((string)((Button)sender).Tag);
    private void ShowSettingsCategory(string category)
    {
        _settingsCategory = category;
        if(category=="Inputs")_ = LoadPlacementBackground();
        foreach (var section in new[] { (RecordingSettings, "Recording"), (AudioSettings, "Audio"), (HighlightsSettings, "Highlights"), (StorageSettings, "Storage"), (InputSettings,"Inputs") }) section.Item1.Visibility = section.Item2 == category ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { RecordingCategory, AudioCategory, HighlightsCategory, StorageCategory,InputsCategory })
        {
            button.Background = (string)button.Tag == category ? (Brush)FindResource("RaisedBrush") : Brushes.Transparent;
            button.Foreground = (Brush)FindResource((string)button.Tag == category ? "TextBrush" : "SecondaryBrush");
        }
        SettingsScroll.ScrollToTop();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject source) { while (source != null && source != TitleBar) { if (source is Button) return; source = VisualTreeHelper.GetParent(source); } } if (e.ClickCount == 2) MaximizeClick(sender, e); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) { if (_fullscreen) SetFullscreen(false); else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _exporting || CaptureActive()) return;
        var dialog=new SaveFileDialog{Title="Export clip",Filter="MP4 video|*.mp4",FileName=$"Echo-{_selected.Session.StartedAt:yyyyMMdd-HHmm}-{_selected.Clip.Id[..6]}.mp4",AddExtension=true,OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        var item=_selected;var start=Timeline.SelectionStart;var end=Timeline.SelectionEnd;var preset=_inputPreset;
        _exporting=true;QuickExportButton.IsEnabled=ExportButton.IsEnabled=false;
        try{ShowToast("Exporting clip…");await Task.Run(()=>ClipExport.Save(item.Clip,start,end,item.Session.InputLogPath,item.Session.Encoder,preset,dialog.FileName,Path.Combine(_paths.Root,"export-cache"),()=>_closing||App.IsExiting||CaptureActive()));ShowToast("Exported "+Path.GetFileName(dialog.FileName));}
        catch(OperationCanceledException){ShowToast("Export stopped. Your original clip is unchanged.");}
        catch(Exception ex){ShowToast("Could not export: "+ex.Message[..Math.Min(300,ex.Message.Length)]);}
        finally{_exporting=false;RefreshStatus();}
    }
    private void LoadSettingsFields()
    {
        ModeBox.SelectedIndex = _settings.Mode == RecordingMode.Clips ? 0 : 1; GameAudioBox.IsChecked = _settings.GameAudio; DesktopAudioBox.IsChecked = _settings.DesktopAudio; MicBox.IsChecked = _settings.Microphone;
        CaptureMethodBox.SelectedIndex = _settings.CaptureMethod == CaptureMethod.Window ? 0 : 1;
        KillsBox.IsChecked = _settings.CaptureKills; AssistsBox.IsChecked = _settings.CaptureAssists; DeathsBox.IsChecked = _settings.CaptureDeaths;
        ShortcutInput.Text = _settings.ManualShortcut; RemoveMaskedBox.IsChecked = _settings.RemoveMaskedHighlights;
        RecordInputsBox.IsChecked = _settings.RecordInputs;
        LoadOverlayFields();
        BeforeInput.Text = _settings.BeforeSeconds.ToString(CultureInfo.InvariantCulture); AfterInput.Text = _settings.AfterSeconds.ToString(CultureInfo.InvariantCulture);
        ResolutionBox.SelectedIndex = _settings.Height == 720 ? 0 : 1; FpsBox.SelectedIndex = _settings.Fps == 30 ? 0 : 1; OutputInput.Text = _settings.OutputDirectory;
    }
    private void BrowseClick(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Choose recording folder" }; if (dialog.ShowDialog(this) == true) { OutputInput.Text = dialog.FolderName; AutoSave(false); } }
    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var before = int.Parse(BeforeInput.Text, CultureInfo.InvariantCulture); var after = int.Parse(AfterInput.Text, CultureInfo.InvariantCulture);
            var buffer = Math.Max(40, before + after + 5);
            var settings = _settings with { Mode = ModeBox.SelectedIndex == 0 ? RecordingMode.Clips : RecordingMode.FullMatch, CaptureMethod = CaptureMethodBox.SelectedIndex == 0 ? CaptureMethod.Window : CaptureMethod.FullscreenDisplay, GameAudio = GameAudioBox.IsChecked == true,
                DesktopAudio = DesktopAudioBox.IsChecked == true, Microphone = MicBox.IsChecked == true, RecordInputs = RecordInputsBox.IsChecked == true, ManualShortcut = ShortcutGesture.Parse(ShortcutInput.Text)?.Text ?? "", RemoveMaskedHighlights = RemoveMaskedBox.IsChecked == true, CaptureKills = KillsBox.IsChecked == true, CaptureAssists = AssistsBox.IsChecked == true,
                CaptureDeaths = DeathsBox.IsChecked == true, BeforeSeconds = before, AfterSeconds = after, BufferSeconds = buffer,
                BufferMegabytes = Math.Max(128, (int)Math.Ceiling(_settings.BitrateKbps * 1000d / 8 * buffer * 1.3 / 1048576)),
                Width = ResolutionBox.SelectedIndex == 0 ? 1280 : 1920, Height = ResolutionBox.SelectedIndex == 0 ? 720 : 1080, Fps = FpsBox.SelectedIndex == 0 ? 30 : 60, OutputDirectory = OutputInput.Text.Trim(),InputOverlay=ReadOverlayFields(),Theme=ThemeBox.SelectedIndex==1?"Light":ThemeBox.SelectedIndex==2?"System":"Dark" };
            settings.Validate(); JsonStore.Write(_paths.Settings, settings); _settings = settings;
            _inputPreset=settings.InputOverlay;ThemeService.Apply(settings.Theme);RefreshOverlayPreview();UpdatePlaybackInputs(Player.Position.TotalSeconds);
            SettingsError.Text = ""; _settingsDirty = false; _catalogSignature = "";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException) { SettingsError.Text = ex.Message; }
    }


}

internal sealed class ClipItem
{
    public Session Session { get; }
    public MediaClip Clip { get; }
    public string Title => Session.Champion;
    public string Label => Clip.Label;
    public bool IsReady => Session.EndedAt != null && !Clip.VideoUnavailable && File.Exists(Clip.Path) && !Clip.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && (!Session.PreparationPending || Clip.Id == Session.Id);
    public string StateLabel => Session.EndedAt == null ? "Recording" : !IsReady ? "Preparing…" : "";
    public Visibility StateVisibility => StateLabel.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    private IEnumerable<Moment> Events => Session.Moments.Where(m => Clip.EventIds != null ? Clip.EventIds.Contains(m.EventId) : PlaybackNavigation.FileTime(Clip, m.RecordingTime) is { } t && t >= Clip.Start && t <= Clip.End);
    public int KillCount => Events.Count(m => m.Kind == MomentKind.Kill);
    public int DeathCount => Events.Count(m => m.Kind == MomentKind.Death);
    public int AssistCount => Events.Count(m => m.Kind == MomentKind.Assist);
    public int ManualCount => Events.Count(m => m.Kind == MomentKind.Manual);
    public Visibility KillVisibility => KillCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeathVisibility => DeathCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AssistVisibility => AssistCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManualVisibility => ManualCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string ShortDate => Session.StartedAt.ToLocalTime().ToString("MMM d, yyyy  HH:mm", CultureInfo.InvariantCulture);
    public string DateLabel => ShortDate;
    public string DurationLabel => TimeSpan.FromSeconds(Math.Max(0, Clip.End - Clip.Start)).ToString(@"mm\:ss");
    private readonly string? _thumbnailPath;
    private WeakReference<BitmapSource>? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail != null && _thumbnail.TryGetTarget(out var cached)) return cached;
            if (_thumbnailPath == null || !File.Exists(_thumbnailPath)) return null;
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 480; bitmap.UriSource = new Uri(_thumbnailPath); bitmap.EndInit(); bitmap.Freeze(); _thumbnail = new(bitmap); return bitmap; }
            catch (Exception e) when (e is IOException or NotSupportedException) { return null; }
        }
    }
    public ClipItem(Session session, MediaClip clip, string? thumbnail) { Session = session; Clip = clip; _thumbnailPath = thumbnail; }
}
