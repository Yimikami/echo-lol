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
using System.Windows.Shapes;
using System.Windows.Threading;
using Echo.Core;
using Echo.Recorder;
using Microsoft.Win32;

namespace Echo.App;

public partial class MainWindow : Window
{
    private readonly HostPaths _paths;
    private EchoSettings _settings;
    private RecorderStatus _status = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<ClipItem> _items = [];
    private ClipItem? _selected;
    private string _filter = "All", _catalogSignature = "";
    private bool _updating, _seeking, _playing, _matches, _exporting;
    private double _duration;
    private int _ticks;

    public MainWindow(HostPaths paths, bool startRecorder)
    {
        _paths = paths;
        try { _settings = paths.LoadSettings(); }
        catch (Exception e) { _settings = new(); MessageBox.Show("Settings could not be loaded: " + e.Message, "Echo"); }
        InitializeComponent(); LoadSettingsFields();
        Loaded += async (_, _) => { if (startRecorder) await EnsureRecorder(); RefreshCatalog(true); RefreshStatus(); };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        Closing += (_, _) => { _timer.Stop(); Player.Close(); };
    }

    private void Tick()
    {
        if (++_ticks % 5 == 0) RefreshStatus();
        if (_ticks % 25 == 0) RefreshCatalog();
        if (_selected == null || _duration <= 0 || _seeking) return;
        _updating = true; Seek.Value = Math.Clamp(Player.Position.TotalSeconds, 0, _duration); _updating = false;
        TimeLabel.Text = $"{Time(Player.Position.TotalSeconds)} / {Time(_duration)}";
        if (_playing && Player.Position.TotalSeconds >= TrimEnd.Value)
        { Player.Pause(); _playing = false; PlayButton.Content = "Play"; }
    }
    private void RefreshStatus()
    {
        try
        {
            var status = JsonStore.Read<RecorderStatus>(_paths.Status);
            bool fresh = status != null && DateTimeOffset.UtcNow - status.UpdatedAt < TimeSpan.FromSeconds(8);
            _status = fresh ? status! : new() { State = "Recorder offline" };
            HeaderStatus.Text = _status.State.Length > 32 ? _status.State[..32] : _status.State;
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_status.Recording ? "#FFAE92" : fresh ? "#9DE1C7" : "#7D8E8B"));
            CaptureButton.Content = !fresh ? "Start recorder" : _status.Armed ? "Pause capture" : "Resume capture";
            RecorderLabel.Text = _status.Recording ? "Recording" : fresh ? "Ready to record" : "Recorder offline";
            RecorderDetail.Text = _status.Recording ? $"{Time(_status.ElapsedSeconds)} · {_status.Encoder}\nGame audio: {(_status.GameAudioHooked == true ? "attached" : _status.GameAudioHooked == false ? "waiting" : "not selected")}" : "Waiting for League.\nClips stay on this computer.";
            ErrorBanner.Text = _status.Error ?? "";
            ExportButton.IsEnabled = _selected != null && !_status.Recording && !_exporting;
            ExportButton.ToolTip = _status.Recording ? "Export is available after recording ends." : "Export the selected range as an MP4";
        }
        catch (Exception e) when (e is IOException or JsonException) { ErrorBanner.Text = "Could not read recorder status: " + e.Message; }
    }
    private void RefreshCatalog(bool force = false)
    {
        try
        {
            if (!Directory.Exists(_settings.OutputDirectory)) { _items.Clear(); ApplyFilter(); return; }
            var files = Directory.EnumerateDirectories(_settings.OutputDirectory).Take(1000)
                .Select(dir => new FileInfo(System.IO.Path.Combine(dir, "session.json"))).Where(f => f.Exists).ToArray();
            var signature = string.Join(";", files.Select(f => $"{f.Name}:{f.Directory!.Name}:{f.LastWriteTimeUtc.Ticks}:{f.Length}"));
            if (!force && signature == _catalogSignature) return;
            _catalogSignature = signature; _items.Clear();
            foreach (var file in files)
            {
                Session? session;
                try { session = JsonStore.Read<Session>(file.FullName); } catch (Exception e) when (e is IOException or JsonException) { continue; }
                if (session == null || session.EndedAt == null) continue;
                if (_matches)
                {
                    if (session.RecordingPath is { } path && File.Exists(path))
                        _items.Add(new(session, new(session.Id, path, 0, session.Duration, "Full match", true), null));
                }
                else foreach (var clip in session.Clips)
                    if (File.Exists(clip.Path)) _items.Add(new(session, clip, System.IO.Path.Combine(file.DirectoryName!, clip.Id + ".jpg")));
            }
            _items.Sort((a, b) => b.Session.StartedAt.CompareTo(a.Session.StartedAt));
            ApplyFilter();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorBanner.Text = "Could not open the recording folder: " + e.Message; }
    }
    private void ApplyFilter()
    {
        if (ClipList == null) return;
        var selectedId = _selected?.Clip.Id;
        var query = SearchBox.Text.Trim();
        var visible = _items.Where(i => (_filter == "All" || i.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || (i.Title + " " + i.DateLabel + " " + i.Label).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        ClipList.ItemsSource = visible;
        EmptyList.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyList.Text = _items.Count == 0 ? (_matches ? "Full-match recordings will appear here.\n\nChoose Full match in recording settings to keep the entire game." : "Your moments will appear here.\n\nEcho saves kills, assists and deaths while you play League.") : "No moments match this filter.";
        if (visible.Length > 0) ClipList.SelectedItem = visible.FirstOrDefault(i => i.Clip.Id == selectedId) ?? visible[0];
    }
    private void ClipSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipItem item) return;
        _selected = item; _duration = 0; _playing = false; PlayButton.Content = "Play";
        Player.Close(); EmptyPlayer.Visibility = Visibility.Collapsed;
        SelectedTitle.Text = item.Title; SelectedDetails.Text = item.DateLabel + "   ·   " + item.Label;
        Filmstrip.Source = item.Thumbnail;
        PlayerPoster.Source = item.Thumbnail; PlayerPoster.Visibility = item.Thumbnail == null ? Visibility.Collapsed : Visibility.Visible;
        ActionStatus.Text = item.Clip.IsVirtual ? "This moment references the full recording. Export creates a separate clip." : "Adjust the range, then export a separate copy.";
        Player.Source = new Uri(item.Clip.Path); Player.Play();
    }
    private void MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !Player.NaturalDuration.HasTimeSpan) return;
        _duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
        _updating = true;
        Seek.Maximum = TrimStart.Maximum = TrimEnd.Maximum = _duration;
        TrimStart.Value = Math.Clamp(_selected.Clip.Start, 0, _duration);
        TrimEnd.Value = Math.Clamp(_selected.Clip.End, TrimStart.Value, _duration);
        Player.Position = TimeSpan.FromSeconds(TrimStart.Value); Player.Pause(); Seek.Value = TrimStart.Value;
        _updating = false; UpdateRange(); DrawMarks(); RefreshStatus();
    }
    private void MediaEnded(object sender, RoutedEventArgs e) { _playing = false; PlayButton.Content = "Play"; }
    private void MediaFailed(object sender, ExceptionRoutedEventArgs e) { ActionStatus.Text = "Windows could not play this file. Use Open file to locate it. " + e.ErrorException.Message; }
    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        if (_playing) { Player.Pause(); _playing = false; }
        else { if (Player.Position.TotalSeconds >= TrimEnd.Value - .1 || Player.Position.TotalSeconds < TrimStart.Value) Player.Position = TimeSpan.FromSeconds(TrimStart.Value); PlayerPoster.Visibility = Visibility.Collapsed; Player.Play(); _playing = true; }
        PlayButton.Content = _playing ? "Pause" : "Play";
    }
    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating && _selected != null && _duration > 0) Player.Position = TimeSpan.FromSeconds(Seek.Value); }
    private void SeekDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekUp(object sender, MouseButtonEventArgs e) { _seeking = false; if (_selected != null) Player.Position = TimeSpan.FromSeconds(Seek.Value); }
    private void TrimChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _duration <= 0) return;
        _updating = true;
        if (TrimStart.Value >= TrimEnd.Value) { if (sender == TrimStart) TrimStart.Value = Math.Max(0, TrimEnd.Value - .1); else TrimEnd.Value = Math.Min(_duration, TrimStart.Value + .1); }
        _updating = false; UpdateRange();
    }
    private void RangeInputChanged(object sender, RoutedEventArgs e)
    {
        if (_duration <= 0) return;
        if (!TryTime(StartInput.Text, out var start) || !TryTime(EndInput.Text, out var end) || start < 0 || end > _duration + .05 || end <= start)
        { ActionStatus.Text = "Use minutes:seconds, with the end after the start and inside the video."; UpdateRange(); return; }
        _updating = true; TrimStart.Value = start; TrimEnd.Value = Math.Min(end, _duration); _updating = false; UpdateRange();
    }
    private void UpdateRange()
    {
        StartInput.Text = Time(TrimStart.Value, true); EndInput.Text = Time(TrimEnd.Value, true);
        RangeLabel.Text = $"{Time(TrimEnd.Value - TrimStart.Value, true)} selected";
    }
    private static string Time(double seconds, bool precise = false)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}" + (precise ? $".{time.Milliseconds / 100}" : "");
    }
    private static bool TryTime(string text, out double seconds)
    {
        seconds = 0; var parts = text.Trim().Split(':');
        if (parts.Length > 3) return false;
        foreach (var part in parts) { if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0) return false; seconds = seconds * 60 + value; }
        return true;
    }
    private void MarksSizeChanged(object sender, SizeChangedEventArgs e) => DrawMarks();
    private void DrawMarks()
    {
        if (EventMarks == null) return;
        EventMarks.Children.Clear();
        if (_selected == null || _duration <= 0) return;
        var clip = _selected.Clip;
        foreach (var m in _selected.Session.Moments)
        {
            var t = m.RecordingTime - clip.RecordingOrigin;
            if (t < 0 || t > _duration) continue;
            var line = new System.Windows.Shapes.Rectangle { Width = 5, Height = 18, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(m.Kind == MomentKind.Death ? "#FFA891" : m.Kind == MomentKind.Assist ? "#A3C8ED" : "#9DE1C7")), ToolTip = $"{m.Kind} · {Time(t)}", Cursor = Cursors.Hand };
            line.MouseLeftButtonDown += (_, _) => { Player.Position = TimeSpan.FromSeconds(t); };
            Canvas.SetLeft(line, t / _duration * EventMarks.ActualWidth); Canvas.SetTop(line, 26); EventMarks.Children.Add(line);
        }
    }
    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _exporting || _status.Recording) return;
        var dialog = new SaveFileDialog { Title = "Export clip", Filter = "MP4 video|*.mp4", FileName = $"Echo-{_selected.Session.StartedAt:yyyyMMdd-HHmm}-{_selected.Clip.Id[..6]}.mp4", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) { ActionStatus.Text = "Choose a new filename; Echo keeps existing videos intact."; return; }
        var input = _selected.Clip.Path; var start = TrimStart.Value; var length = TrimEnd.Value - start;
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
                    if (JsonStore.Read<RecorderStatus>(_paths.Status)?.Recording == true) { p.Kill(true); throw new OperationCanceledException("Export stopped because a recording started. Try again after the match."); }
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
    private void RevealClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; info.ArgumentList.Add("/select," + _selected.Clip.Path); Process.Start(info);
    }
    private async Task EnsureRecorder()
    {
        if (await Command("status")) return;
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, "Echo.Recorder.exe");
        if (!File.Exists(file)) { ErrorBanner.Text = "Recorder is missing. Run scripts/build.ps1 to assemble Echo."; return; }
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(_paths.Root);
        Process.Start(info)?.Dispose();
        await Task.Delay(800); RefreshStatus();
    }
    private async Task<bool> Command(string command)
    {
        try
        {
            using var timeout = new CancellationTokenSource(1200);
            await using var client = new NamedPipeClientStream(".", _paths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
            using var reader = new StreamReader(client, leaveOpen: true); var reply = await reader.ReadToEndAsync(timeout.Token);
            _ = JsonSerializer.Deserialize<RecorderStatus>(reply, JsonStore.Options); return true;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or JsonException) { return false; }
    }
    private async void CaptureClick(object sender, RoutedEventArgs e) { if (DateTimeOffset.UtcNow - _status.UpdatedAt > TimeSpan.FromSeconds(8) || _status.State == "Recorder offline") await EnsureRecorder(); else await Command(_status.Armed ? "disarm" : "arm"); }
    private async void SaveMomentClick(object sender, RoutedEventArgs e) { ActionStatus.Text = await Command("save") && _status.Recording ? "Manual moment requested." : "Start a League recording before saving a moment."; }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterClick(object sender, RoutedEventArgs e) { _filter = (string)((Button)sender).Tag; ApplyFilter(); }
    private void LibraryClick(object sender, RoutedEventArgs e) { _matches = false; PageTitle.Text = "Library"; SettingsPanel.Visibility = Visibility.Collapsed; LibraryPanel.Visibility = Visibility.Visible; RefreshCatalog(true); }
    private void MatchesClick(object sender, RoutedEventArgs e) { _matches = true; PageTitle.Text = "Matches"; SettingsPanel.Visibility = Visibility.Collapsed; LibraryPanel.Visibility = Visibility.Visible; RefreshCatalog(true); }
    private void SettingsClick(object sender, RoutedEventArgs e) { PageTitle.Text = "Settings"; LibraryPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible; LoadSettingsFields(); }
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
            SettingsMessage.Text = "Saved. These settings apply to the next recording."; _catalogSignature = "";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException) { SettingsMessage.Text = ex.Message; }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

internal sealed class ClipItem
{
    public Session Session { get; }
    public MediaClip Clip { get; }
    public string Title => Session.Champion;
    public string Label => Clip.Label;
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
