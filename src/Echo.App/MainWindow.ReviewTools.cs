using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Echo.Core;
using Echo.App.Controls;
using Echo.Recorder;
using Microsoft.Win32;

namespace Echo.App;

public partial class MainWindow
{
    private static string KindName(MomentKind kind) => kind switch
    {
        MomentKind.MultiKill => "Multikills",
        MomentKind.Ace => "Aces",
        MomentKind.Manual => "Bookmarks and manual clips",
        _ => kind + "s"
    };

    private void HighlightRulesClick(object sender, RoutedEventArgs e)
    {
        if (_settingsDirty) SaveSettingsClick(this, new());
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Set how much footage to keep for each automatic event.", TextWrapping = TextWrapping.Wrap });
        var rows = new List<(MomentKind Kind, CheckBox Enabled, CheckBox Custom, TextBox Before, TextBox After)>();
        var heading = new UniformGridLabels("Event", "Custom length", "Before (s)", "After (s)");
        panel.Children.Add(heading);
        foreach (var kind in Enum.GetValues<MomentKind>().Where(k => k != MomentKind.Manual))
        {
            var rule = _settings.Rule(kind);
            bool basic = kind is MomentKind.Kill or MomentKind.Assist or MomentKind.Death;
            var enabled = new CheckBox { Content = KindName(kind), IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center };
            var custom = new CheckBox { IsChecked = _settings.HighlightRules.TryGetValue(kind, out var storedRule) && !storedRule.UseDefaultLength, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(custom, $"Custom length for {KindName(kind)}");
            var before = new TextBox { Text = rule.Before.ToString(CultureInfo.InvariantCulture), MaxLength = 2, Margin = new(0, 0, 8, 0) };
            var after = new TextBox { Text = rule.After.ToString(CultureInfo.InvariantCulture), MaxLength = 2 };
            System.Windows.Automation.AutomationProperties.SetName(before, $"Seconds before {KindName(kind)}");
            System.Windows.Automation.AutomationProperties.SetName(after, $"Seconds after {KindName(kind)}");
            if (!basic && !rule.Enabled && rule.Before == _settings.BeforeSeconds && rule.After == _settings.AfterSeconds) custom.IsChecked = false;
            void Update() => before.IsEnabled = after.IsEnabled = custom.IsChecked == true;
            custom.Checked += (_, _) => Update();
            custom.Unchecked += (_, _) => Update();
            Update();
            var row = new Border { CornerRadius = new(12), Padding = new(12, 5, 12, 10), Margin = new(0, 4, 0, 0),
                Child = new UniformGridLabels(enabled, custom, before, after) };
            row.SetResourceReference(BackgroundProperty, "RaisedBrush");
            panel.Children.Add(row);
            rows.Add((kind, enabled, custom, before, after));
        }
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new(0, 9, 0, 0) };
        panel.Children.Add(error);
        EchoSettings? saved = null;
        if (!ShowToolDialog("Automatic highlights", panel, "Apply", 680, () =>
        {
            var rules = new Dictionary<MomentKind, HighlightRule>();
            foreach (var (kind, enabled, custom, before, after) in rows)
            {
                bool isEnabled = enabled.IsChecked == true;
                int lead = _settings.BeforeSeconds, tail = _settings.AfterSeconds;
                if (custom.IsChecked == true && (!int.TryParse(before.Text, out lead) || !int.TryParse(after.Text, out tail)))
                {
                    error.Text = $"Enter a whole number for {KindName(kind).ToLowerInvariant()}.";
                    return false;
                }
                if (custom.IsChecked == true || !isEnabled && kind is not (MomentKind.Kill or MomentKind.Assist or MomentKind.Death))
                    rules[kind] = new(isEnabled, lead, tail) { UseDefaultLength = custom.IsChecked != true };
            }
            saved = (_settings with { HighlightRules = rules, CaptureKills = rows[0].Enabled.IsChecked == true,
                CaptureAssists = rows[1].Enabled.IsChecked == true, CaptureDeaths = rows[2].Enabled.IsChecked == true }).FitReplayBuffer();
            try { saved.Validate(); return true; }
            catch (ArgumentException ex) { error.Text = ex.Message; return false; }
        })) return;
        try
        {
            PersistToolSettings(saved!);
            LoadCaptureChoices();
            SettingsError.Text = "";
        }
        catch (Exception ex) { ShowToast(ShortError(ex)); }
    }

    private sealed class UniformGridLabels : Grid
    {
        public UniformGridLabels(params object[] values)
        {
            Margin = new(0, 8, 0, 0);
            MinHeight = 44;
            foreach (int width in new[] { 200, 135, 100, 100 }) ColumnDefinitions.Add(new() { Width = new(width) });
            for (int i = 0; i < values.Length; i++)
            {
                var element = values[i] as UIElement ?? new TextBlock { Text = (string)values[i], Margin = new(0, 8, 0, 3) };
                SetColumn(element, i);
                Children.Add(element);
            }
        }
    }

    private async void AddReviewMoment(ClipItem item, bool saveClip)
    {
        if (_duration <= 0 || _selected?.Entry != item.Entry) return;
        SetPlaying(false);
        var panel = new StackPanel();
        var title = new TextBox { MaxLength = 120 };
        AddField(panel, saveClip ? "Clip title (optional)" : "Bookmark name (optional)", title);
        var playhead = Math.Clamp(Player.Position.TotalSeconds, item.Clip.Start, item.Clip.End);
        var range = new TimeRange(Timeline.SelectionStart, Timeline.SelectionEnd);
        if (saveClip && Math.Abs(range.Start - item.Clip.Start) < .05 && Math.Abs(range.End - item.Clip.End) < .05)
        {
            var rule = _settings.Rule(MomentKind.Manual);
            range = new(Math.Max(item.Clip.Start, playhead - rule.Before), Math.Min(item.Clip.End, playhead + rule.After));
        }
        if (saveClip) panel.Children.Add(new TextBlock { Text = $"{Time(range.Start)} – {Time(range.End)}", Margin = new(0, 10, 0, 0) });
        if (!ShowToolDialog(saveClip ? "Save library clip" : "Add bookmark", panel, saveClip ? "Save clip" : "Add bookmark")) return;
        if (saveClip && range.Duration < .15) { ShowToast("Choose a longer range."); return; }
        var root = _settings.OutputDirectory;
        string? name = title.Text.Trim() is { Length: > 0 } text ? text : null;
        var recordingTime = GameplayInputs.RecordingTime(item.Clip, saveClip ? Math.Clamp(playhead, range.Start, range.End) : playhead);
        if (await RunLibraryJob(() =>
        {
            LibraryCatalog.Edit(root, [new(item.Session.Id, item.Session.Id, true)], (session, _) =>
            {
                if (session.DeletedAt != null) throw new IOException("Restore this session first.");
                var id = session.Moments.Where(m => m.EventId < 0).Select(m => m.EventId).DefaultIfEmpty(0).Min() - 1;
                var nearest = session.Moments.MinBy(m => Math.Abs(m.RecordingTime - recordingTime));
                var gameTime = Math.Max(0, nearest == null ? recordingTime : nearest.GameTime + recordingTime - nearest.RecordingTime);
                var moment = new Moment(id, MomentKind.Manual, gameTime, recordingTime) { Note = name ?? "Bookmark" };
                session.Moments.Add(moment);
                if (saveClip)
                {
                    var events = session.Moments.Where(m => PlaybackNavigation.FileTime(item.Clip, m.RecordingTime) is { } at && at >= range.Start && at <= range.End)
                        .Select(m => m.EventId).Distinct().ToArray();
                    session.Clips.Add(item.Clip with { Id = Guid.NewGuid().ToString("N"), Start = range.Start, End = range.End, IsVirtual = true,
                        Label = "Saved clip", EventIds = events, Title = name ?? "Saved clip", Favorite = false, Tags = [], DeletedAt = null,
                        ThumbnailPath = item.Clip.ThumbnailPath ?? (item.WholeMatch ? Path.Combine(LibraryCatalog.Folder(root, session.Id), "match.jpg") : null) });
                }
                return null;
            });
            return saveClip ? "Clip added to your library." : "Bookmark added.";
        }))
        {
            ReloadSelection();
            if (_selected != null)
            {
                SetTimelineMoments(_selected);
                Timeline.InvalidateVisual();
                RefreshPlaybackActivity();
            }
        }
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } item || _duration <= 0) return;
        ExportOptions? initial = _trimOpen ? new(Layout: TrimFormat.SelectedIndex switch
        {
            1 => ExportLayout.Square,
            2 => TrimHud.IsChecked == true ? ExportLayout.PortraitHud : ExportLayout.Portrait,
            _ => ExportLayout.Original
        }, Overlay: TrimOverlay.IsChecked == true) : null;
        await ExportVideo(item, Timeline.SelectionStart, Timeline.SelectionEnd, Player.Position.TotalSeconds, initial);
    }

    private async void FeaturedExportClick(object sender, RoutedEventArgs e)
    {
        if (FeaturedPanel.DataContext is ClipItem { IsReady: true } item)
            await ExportVideo(item, item.Clip.Start, item.Clip.End, (item.Clip.Start + item.Clip.End) / 2);
    }

    private async Task ExportVideo(ClipItem item, double start, double end, double position, ExportOptions? initial = null)
    {
        if (_exporting || _libraryBusy || CaptureActive()) return;
        SetPlaying(false);
        var options = await ChooseExportOptions(item, single: true, position: position, initial: initial);
        if (options == null || StopMediaWork()) return;
        var dialog = NewMp4Dialog(item.Title);
        if (dialog.ShowDialog(this) != true || !ValidateExportDestination(dialog.FileName)) return;
        var preset = _inputPreset;
        await RunExport(() =>
        {
            ClipExport.Save(item.Clip, start, end, item.Session.InputLogPath, item.Session.Encoder,
                preset, dialog.FileName, ExportCache, StopMediaWork, options);
            return $"Exported {Path.GetFileName(dialog.FileName)}.";
        });
    }

    private async void ExportSelection(ClipItem[] items)
    {
        if (items.Length == 0 || _libraryBusy || _exporting || CaptureActive()) return;
        var options = await ChooseExportOptions(items[0], single: false);
        if (options == null || StopMediaWork()) return;
        var dialog = new OpenFolderDialog { Title = "Choose an export folder" };
        if (dialog.ShowDialog(this) != true || !ValidateExportDestination(Path.Combine(dialog.FolderName, "clip.mp4"))) return;
        var preset = _inputPreset;
        await RunExport(() =>
        {
            int exported = 0;
            var failures = new List<string>();
            foreach (var item in items)
            {
                if (StopMediaWork()) return $"Export stopped for gameplay. Saved {exported} of {items.Length}.";
                var target = AvailableExport(dialog.FolderName, $"{SafeName(item.Title)}-{item.Session.StartedAt:yyyyMMdd-HHmmss}");
                try
                {
                    ClipExport.Save(item.Clip, item.Clip.Start, item.Clip.End, item.Session.InputLogPath, item.Session.Encoder,
                        preset, target, ExportCache, StopMediaWork, options, false);
                    exported++;
                }
                catch (OperationCanceledException) { return $"Export stopped for gameplay. Saved {exported} of {items.Length}."; }
                catch (Exception ex) { failures.Add(ShortError(ex)); }
            }
            return failures.Count == 0 ? $"Exported {exported} items." : $"Exported {exported} of {items.Length}. {failures[0]}";
        });
    }

    private void RecapClick(object sender, RoutedEventArgs e) { if (_selected is { } item) ExportRecap(item); }

    private async void ExportRecap(ClipItem item)
    {
        if (!item.WholeMatch || _libraryBusy || _exporting || CaptureActive()) return;
        SetPlaying(false);
        var kinds = item.Session.Moments.Select(m => m.Kind).Distinct().Order().ToDictionary(kind => kind, _ => true);
        var options = await ChooseExportOptions(item, single: true, kinds);
        if (options == null || StopMediaWork()) return;
        var ranges = MatchReview.RecapRanges(item.Session, kinds.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet(), _settings);
        if (ranges.Count == 0) { ShowToast("Choose events with available footage."); return; }
        var dialog = NewMp4Dialog(item.Title + " recap");
        if (dialog.ShowDialog(this) != true || !ValidateExportDestination(dialog.FileName)) return;
        var preset = _inputPreset;
        IProgress<RecapProgress> progress = new Progress<RecapProgress>(UpdateRecapProgress);
        await RunExport(() =>
        {
            ClipExport.SaveRecap(item.Session, ranges, preset, dialog.FileName, ExportCache, StopMediaWork, options, progress.Report);
            return $"Exported {Path.GetFileName(dialog.FileName)}.";
        }, recap: true);
    }

    private string ExportCache => Path.Combine(_paths.Root, "export-cache");

    private void UpdateRecapProgress(RecapProgress progress)
    {
        if (RecapProgressPanel.Visibility != Visibility.Visible) return;
        RecapProgressBar.IsIndeterminate = progress.Finalizing;
        if (progress.Finalizing)
        {
            RecapProgressDetail.Text = "Finishing video…";
            RecapProgressPercent.Text = "";
            return;
        }

        var percent = (int)Math.Clamp(progress.RenderedSeconds * 100 / progress.TotalSeconds, 0, 99);
        RecapProgressBar.Value = percent;
        RecapProgressPercent.Text = $"{percent}%";
        RecapProgressDetail.Text = $"Rendering segment {progress.Part} of {progress.PartCount}";
    }

    private async Task RunExport(Func<string> job, bool recap = false)
    {
        if (_exporting || _libraryBusy || CaptureActive()) return;
        _exporting = true;
        QuickExportButton.IsEnabled = ExportButton.IsEnabled = RecapButton.IsEnabled = false;
        UpdateSelectionToolbar();
        if (recap)
        {
            RecapProgressBar.Value = 0;
            RecapProgressBar.IsIndeterminate = true;
            RecapProgressDetail.Text = "Preparing clips…";
            RecapProgressPercent.Text = "";
            RecapProgressPanel.Visibility = Visibility.Visible;
        }
        else ShowToast("Exporting…");
        try { ShowToast(await Task.Run(job)); }
        catch (OperationCanceledException) { ShowToast("Export stopped for gameplay."); }
        catch (Exception ex) { ShowError("Couldn't export", ex); }
        finally
        {
            RecapProgressPanel.Visibility = Visibility.Collapsed;
            RecapProgressBar.IsIndeterminate = false;
            _exporting = false;
            RefreshStatus();
            UpdateSelectionToolbar();
        }
    }

    private static string SafeName(string title) => string.Join("-", title.Split(Path.GetInvalidFileNameChars())).Trim(' ', '.') is { Length: > 0 } safe
        ? safe[..Math.Min(75, safe.Length)] : "Echo clip";

    private static SaveFileDialog NewMp4Dialog(string title) => new() { Title = "Export video", Filter = "MP4 video|*.mp4",
        FileName = SafeName(title) + ".mp4", AddExtension = true, OverwritePrompt = true };

    private static string AvailableExport(string folder, string title)
    {
        var target = Path.Combine(folder, title + ".mp4");
        for (int suffix = 2; File.Exists(target); suffix++) target = Path.Combine(folder, $"{title} ({suffix}).mp4");
        return target;
    }

    private bool ValidateExportDestination(string file)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_settings.OutputDirectory));
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Choose a folder outside Echo’s managed recording folders.");
            return false;
        }
        return true;
    }

    private async Task<ExportOptions?> ChooseExportOptions(ClipItem item, bool single, Dictionary<MomentKind, bool>? kinds = null,
        double? position = null, ExportOptions? initial = null)
    {
        var previewTime = position ?? (single ? Player.Position.TotalSeconds : (item.Clip.Start + item.Clip.End) / 2);
        var poster = item.Thumbnail;
        if (!StopMediaWork())
        {
            try
            {
                var path = Path.Combine(ExportCache, $"{Guid.NewGuid():N}.jpg");
                poster = await Task.Run(() =>
                {
                    Directory.CreateDirectory(ExportCache);
                    try
                    {
                        MediaTools.Thumbnail(item.Clip.Path, path, Math.Clamp(previewTime, item.Clip.Start, Math.Max(item.Clip.Start, item.Clip.End - .15)), 600, StopMediaWork);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                        return bitmap;
                    }
                    finally { File.Delete(path); }
                });
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or NotSupportedException) { }
        }
        if (StopMediaWork()) return null;
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new() { Width = new(265) });
        columns.ColumnDefinitions.Add(new() { Width = new(24) });
        columns.ColumnDefinitions.Add(new());
        var preview = new ExportFramePreview { Frame = poster, Height = 320 };
        columns.Children.Add(preview);
        var form = new StackPanel();
        Grid.SetColumn(form, 2);
        columns.Children.Add(form);
        var format = new ComboBox { ItemsSource = new[] { "Original", "Square · 1:1", "Portrait · 9:16", "Portrait with League HUD" }, SelectedIndex = (int)(initial?.Layout ?? ExportLayout.Original) };
        AddField(form, "Format", format);
        var focus = new Slider { Minimum = 0, Maximum = 1, Value = .5, IsMoveToPointEnabled = true };
        var vertical = new Slider { Minimum = 0, Maximum = 1, Value = .5, IsMoveToPointEnabled = true };
        var left = new CheckBox { Content = "Minimap is on the left", Margin = new(0, 9, 0, 0) };
        AddField(form, "Horizontal framing", focus);
        AddField(form, "Vertical framing", vertical);
        form.Children.Add(left);
        var overlay = new CheckBox { Content = "Include input overlay", IsChecked = _inputPreset.Enabled && initial?.Overlay != false,
            IsEnabled = _inputPreset.Enabled && (!single || File.Exists(item.Session.InputLogPath)), Margin = new(0, 13, 0, 0) };
        form.Children.Add(overlay);
        var audioChoices = single && item.Session.AudioTracks.Length > 1
            ? new[] { "Original mix", "Adjust sources", "Keep all tracks", "Mute" }
            : new[] { "Original mix", "Keep all tracks", "Mute" };
        var audio = new ComboBox { ItemsSource = audioChoices, SelectedIndex = 0 };
        AddField(form, "Audio", audio);
        var audioPanel = new StackPanel { Margin = new(0, 5, 0, 0) };
        var gains = new List<Slider>();
        foreach (var track in item.Session.AudioTracks.Skip(1))
        {
            var slider = new Slider { Minimum = 0, Maximum = 2, Value = track.StartsWith("Desktop", StringComparison.Ordinal) && item.Session.AudioTracks.Contains("Game") ? 0 : 1,
                TickFrequency = .05, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
            var gain = new TextBlock();
            void UpdateGain() => gain.Text = $"{track}   {slider.Value:P0}";
            UpdateGain();
            slider.ValueChanged += (_, _) => UpdateGain();
            audioPanel.Children.Add(gain);
            audioPanel.Children.Add(slider);
            gains.Add(slider);
        }
        form.Children.Add(audioPanel);
        var eventChoices = new Dictionary<MomentKind, CheckBox>();
        if (kinds != null)
        {
            form.Children.Add(new TextBlock { Text = "Include in recap", FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 6) });
            foreach (var kind in kinds.Keys)
            {
                var choice = new CheckBox { Content = KindName(kind), IsChecked = true, Margin = new(0, 0, 0, 5) };
                eventChoices[kind] = choice;
                form.Children.Add(choice);
            }
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed, Margin = new(0, 9, 0, 0) };
        form.Children.Add(error);
        string AudioMode() => (string)audio.SelectedItem;
        ExportLayout Layout() => (ExportLayout)format.SelectedIndex;
        void UpdatePreview()
        {
            bool cropping = Layout() is ExportLayout.Square or ExportLayout.Portrait;
            focus.IsEnabled = vertical.IsEnabled = cropping;
            left.Visibility = Layout() == ExportLayout.PortraitHud ? Visibility.Visible : Visibility.Collapsed;
            audioPanel.Visibility = AudioMode() == "Adjust sources" ? Visibility.Visible : Visibility.Collapsed;
            preview.Options = new(Layout(), focus.Value, vertical.Value, left.IsChecked == true);
            preview.InvalidateVisual();
        }
        format.SelectionChanged += (_, _) => UpdatePreview();
        audio.SelectionChanged += (_, _) => UpdatePreview();
        focus.ValueChanged += (_, _) => UpdatePreview();
        vertical.ValueChanged += (_, _) => UpdatePreview();
        left.Checked += (_, _) => UpdatePreview();
        left.Unchecked += (_, _) => UpdatePreview();
        preview.Reposition += (x, y) => { focus.Value = x; vertical.Value = y; };
        UpdatePreview();
        if (!ShowToolDialog(kinds == null ? "Export video" : "Create match recap", columns, "Choose destination", 755, () =>
        {
            if (kinds != null && !eventChoices.Values.Any(choice => choice.IsChecked == true)) { error.Text = "Select at least one event."; return false; }
            int game = Array.IndexOf(item.Session.AudioTracks, "Game") - 1;
            int desktop = Array.IndexOf(item.Session.AudioTracks, "Desktop (includes game)") - 1;
            if (AudioMode() == "Adjust sources" && game >= 0 && desktop >= 0 && gains[game].Value > 0 && gains[desktop].Value > 0)
            {
                error.Text = "Choose Game or Desktop to avoid playing League audio twice.";
                return false;
            }
            return true;
        })) return null;
        if (kinds != null) foreach (var (kind, choice) in eventChoices) kinds[kind] = choice.IsChecked == true;
        double[]? mix = AudioMode() == "Adjust sources" ? [0, ..gains.Select(g => g.Value)] : null;
        return new(Layout(), focus.Value, vertical.Value, left.IsChecked == true, mix, AudioMode() == "Keep all tracks", overlay.IsChecked == true, AudioMode() == "Mute");
    }
}
