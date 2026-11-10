using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Echo.Core;
using Echo.Recorder;
using Microsoft.Win32;

namespace Echo.App;

public partial class MainWindow
{
    private ClipItem[] _filteredItems = [];
    private readonly HashSet<LibraryEntry> _selection = [];
    private bool _selecting, _libraryBusy;
    private int _libraryView;
    private string? _managedCatalog;
    private long _lastMediaProbe;
    private volatile bool _gameplayDuringMediaWork;

    private static ClipItem MatchItem(Session session, string folder) =>
        new(session, new(session.Id, session.RecordingPath ?? "", 0, session.Duration,
            session.RecordingPath == null ? "Recording session" : session.Champion == "Imported video" ? "Imported video" : "Full match", true),
            FindMatchPoster(folder, session), true);

    private bool ShowEntry(ClipItem item) => _libraryView == 3
        ? item.Session.EndedAt != null && !item.Session.PreparationPending && item.Missing
        : !item.Missing && !item.Clip.VideoUnavailable && (_libraryView != 1 || item.Favorite);

    private void LibraryViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList == null || MomentFilters == null || LibraryView == null) return;
        _libraryView = Math.Max(0, LibraryView.SelectedIndex);
        MomentFilters.Visibility = _libraryView < 2 ? Visibility.Visible : Visibility.Collapsed;
        _galleryPage = 0;
        SetSelectionMode(false, refresh: false);
        RefreshCatalog(true);
    }

    private void SetSelectionMode(bool selecting, bool refresh = true)
    {
        _selecting = selecting;
        if (!selecting) _selection.Clear();
        _bindingGallery = true;
        ClipList.SelectionMode = selecting ? SelectionMode.Extended : SelectionMode.Single;
        _bindingGallery = false;
        SelectModeButton.Content = selecting ? "Done" : "Select";
        SelectionToolbar.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        if (refresh) ApplyFilter();
    }

    private void MoreEventsClick(object sender, RoutedEventArgs e)
    {
        var kinds = Enum.GetValues<MomentKind>().Where(kind => kind is not (MomentKind.Kill or MomentKind.Assist or MomentKind.Death));
        OpenMenu(MoreEventsButton, kinds.Select(kind => (KindName(kind), true, (Action)(() =>
        {
            _filter = kind.ToString();
            SetFilter();
            ApplyFilter(true);
        }))).ToArray());
    }

    private void SelectModeClick(object sender, RoutedEventArgs e) => SetSelectionMode(!_selecting);

    private void GalleryMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ClipList, source) is not ListBoxItem card) return;
        card.Focus();
        card.IsSelected = !card.IsSelected;
        e.Handled = true;
    }

    private void SelectPageClick(object sender, RoutedEventArgs e)
    {
        _selection.UnionWith(ClipList.Items.Cast<ClipItem>().Select(i => i.Entry));
        ApplyFilter();
    }

    private void SelectResultsClick(object sender, RoutedEventArgs e)
    {
        _selection.UnionWith(_filteredItems.Select(i => i.Entry));
        ApplyFilter();
    }

    private void ClearSelectionClick(object sender, RoutedEventArgs e) { _selection.Clear(); ApplyFilter(); }
    private void UpdateSelectionToolbar()
    {
        if (SelectionCount == null) return;
        SelectionCount.Text = $"{_selection.Count} selected";
        SelectionActions.IsEnabled = _selection.Count > 0 && !_libraryBusy && !_exporting;
    }

    private static void OpenMenu(Button anchor, params (string Label, bool Enabled, Action Run)[] actions)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, enabled, run) in actions)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        anchor.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void SelectionActionsClick(object sender, RoutedEventArgs e)
    {
        var items = _filteredItems.Where(item => _selection.Contains(item.Entry)).ToArray();
        if (items.Length == 0) return;
        bool editable = items.All(item => item.Session.EndedAt != null && !item.Session.PreparationPending);
        if (_libraryView == 2)
        {
            OpenMenu(SelectionActions, ("Restore selected", editable, () => _ = TrashItems(items, false)));
            return;
        }
        if (_libraryView == 3)
        {
            OpenMenu(SelectionActions,
                ("Relink video…", editable && items.Length == 1, () => RelinkItem(items[0])),
                ("Move to Trash…", editable, () => _ = TrashItems(items, true)));
            return;
        }
        OpenMenu(SelectionActions,
            ("Export selected…", editable && items.All(i => i.IsReady) && !CaptureActive(), () => ExportSelection(items)),
            ("Add to favorites", editable, () => FavoriteItems(items, true)),
            ("Remove from favorites", editable, () => FavoriteItems(items, false)),
            ("Add tags…", editable, () => EditDetails(items, false)),
            ("Move to Trash…", editable, () => _ = TrashItems(items, true)));
    }

    private void LibraryMenuClick(object sender, RoutedEventArgs e)
    {
        bool idle = !_libraryBusy && !_exporting && !CaptureActive();
        OpenMenu(LibraryMenuButton,
            ("Import MP4…", idle, ImportClick),
            ("Back up library…", idle, BackupClick),
            ("Restore backup…", idle, RestoreBackupClick),
            ("Storage usage and cleanup…", idle, () => StorageToolsClick(this, new())),
            ("Clean up to storage limit…", idle && _settings.LibraryLimitMegabytes > 0, CleanToLimitClick),
            ("Empty Trash…", idle, EmptyTrashClick));
    }

    private void ReviewMenuClick(object sender, RoutedEventArgs e)
    {
        var item = _selected;
        if (item == null) return;
        bool editable = item.Session.EndedAt != null && !item.Session.PreparationPending && !_libraryBusy && !_exporting;
        OpenMenu(ReviewMenuButton,
            (item.Favorite ? "Remove from favorites" : "Add to favorites", editable, () => FavoriteItems([item], !item.Favorite)),
            ("Edit title and tags…", editable, () => EditDetails([item], true)),
            ("Add bookmark at playhead…", editable && _duration > 0, () => AddReviewMoment(item, false)),
            ("Save range as a library clip…", editable && _duration > 0, () => AddReviewMoment(item, true)),
            ("Create match recap…", editable && item.WholeMatch && item.Session.Moments.Count > 0 && !CaptureActive(), () => ExportRecap(item)),
            ("Compare match APM…", item.WholeMatch && File.Exists(item.Session.InputLogPath) && !CaptureActive(), () => ShowApmComparison(item)));
    }

    private async Task TrashItems(ClipItem[] items, bool deleted)
    {
        if (deleted && MessageBox.Show(this, $"Move {items.Length} selected item(s) to Echo’s Trash?", "Move to Trash",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var root = _settings.OutputDirectory;
        await RunLibraryJob(() =>
        {
            LibraryCatalog.SetTrash(root, items.Select(i => i.Entry), deleted);
            return deleted ? $"Moved {items.Length} item(s) to Trash." : $"Restored {items.Length} item(s).";
        });
    }

    private async void FavoriteItems(ClipItem[] items, bool favorite)
    {
        var root = _settings.OutputDirectory;
        if (await RunLibraryJob(() =>
        {
            LibraryCatalog.Edit(root, items.Select(i => i.Entry), (session, clip) =>
            {
                if (clip != null) return clip with { Favorite = favorite };
                session.Favorite = favorite;
                return null;
            });
            return favorite ? "Added to favorites." : "Removed from favorites.";
        })) ReloadSelection();
    }

    private async void EditDetails(ClipItem[] items, bool titleEnabled)
    {
        var panel = new StackPanel();
        var title = new TextBox { Text = items[0].WholeMatch ? items[0].Session.Title ?? "" : items[0].Clip.Title ?? "", MaxLength = 120 };
        if (titleEnabled) AddField(panel, "Title", title);
        var tags = new TextBox { Text = titleEnabled ? string.Join(", ", items[0].Tags) : "", MaxLength = 500 };
        AddField(panel, "Tags, separated by commas", tags);
        if (!ShowToolDialog(titleEnabled ? "Edit details" : "Add tags", panel, "Apply")) return;
        var root = _settings.OutputDirectory;
        string? caption = title.Text.Trim() is { Length: > 0 } text ? text : null;
        var values = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag[..Math.Min(40, tag.Length)]).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        if (await RunLibraryJob(() =>
        {
            LibraryCatalog.Edit(root, items.Select(i => i.Entry), (session, clip) =>
            {
                string[] Tags(string[] previous) => titleEnabled ? values : previous.Concat(values).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
                if (clip != null) return clip with { Title = titleEnabled ? caption : clip.Title, Tags = Tags(clip.Tags) };
                if (titleEnabled) session.Title = caption;
                session.Tags = Tags(session.Tags);
                return null;
            });
            return "Details updated.";
        })) ReloadSelection();
    }

    private void ReloadSelection()
    {
        if (_selected == null) return;
        var item = _items.FirstOrDefault(i => i.Entry == _selected.Entry);
        if (item == null) return;
        _selected = item;
        SelectedTitle.Text = item.Title;
        SelectedDetails.Text = item.DateLabel + "   ·   " + item.Subtitle;
    }

    private async Task<bool> RunLibraryJob(Func<string> job)
    {
        if (_libraryBusy || _exporting) return false;
        _libraryBusy = true;
        UpdateSelectionToolbar();
        try
        {
            ShowToast("Working…");
            ShowToast(await Task.Run(job));
            return true;
        }
        catch (OperationCanceledException) { ShowToast("Stopped for gameplay."); return false; }
        catch (Exception ex) { ShowToast($"Could not finish: {ShortError(ex)}"); return false; }
        finally
        {
            _libraryBusy = false;
            RefreshCatalog(true);
            UpdateSelectionToolbar();
        }
    }

    private static string ShortError(Exception ex) => ex.Message[..Math.Min(240, ex.Message.Length)];
    private bool StopMediaWork()
    {
        if (_closing || App.IsExiting) return true;
        long now = Stopwatch.GetTimestamp();
        long previous = Volatile.Read(ref _lastMediaProbe);
        if (Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromMilliseconds(200) &&
            Interlocked.CompareExchange(ref _lastMediaProbe, now, previous) == previous)
            _gameplayDuringMediaWork = CaptureActive();
        return _gameplayDuringMediaWork;
    }

    private async void ImportClick()
    {
        var source = ChooseMp4("Import video");
        if (source == null) return;
        var root = _settings.OutputDirectory;
        if (await RunLibraryJob(() =>
        {
            if (StopMediaWork()) throw new OperationCanceledException();
            var duration = MediaTools.Duration(source);
            LibraryCatalog.Import(root, source, duration, (file, poster) => MediaTools.Thumbnail(file, poster, Math.Min(3, duration / 2), cancel: StopMediaWork), StopMediaWork);
            return "Video imported. Find it in Matches.";
        })) MatchesClick(this, new());
    }

    private async void RelinkItem(ClipItem item)
    {
        var source = ChooseMp4("Find the original video");
        if (source == null) return;
        var root = _settings.OutputDirectory;
        await RunLibraryJob(() =>
        {
            _ = MediaTools.VideoSize(source);
            LibraryCatalog.Relink(root, item.Entry, source, MediaTools.Duration(source), StopMediaWork);
            return "Video relinked.";
        });
    }

    private string? ChooseMp4(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = "MP4 video|*.mp4", CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void BackupClick()
    {
        var dialog = new SaveFileDialog { Title = "Back up Echo library", Filter = "Echo backup|*.zip", FileName = $"Echo-library-{DateTime.Now:yyyy-MM-dd}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        var root = _settings.OutputDirectory;
        await RunLibraryJob(() =>
        {
            LibraryBackup.Save(root, dialog.FileName, StopMediaWork);
            return "Library backup saved.";
        });
    }

    private async void RestoreBackupClick()
    {
        var dialog = new OpenFileDialog { Title = "Restore Echo backup", Filter = "Echo backup|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var root = _settings.OutputDirectory;
        await RunLibraryJob(() => $"Restored {LibraryBackup.Restore(root, dialog.FileName, StopMediaWork)} recording session(s).");
    }

    private async void EmptyTrashClick()
    {
        if (MessageBox.Show(this, "Permanently remove everything in Echo’s Trash?", "Empty Trash",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var root = _settings.OutputDirectory;
        await RunLibraryJob(() =>
        {
            foreach (var session in LibraryCatalog.Read(root).Where(s => s.DeletedAt != null || s.Clips.Any(c => c.DeletedAt != null)))
            {
                if (StopMediaWork()) throw new OperationCanceledException();
                LibraryCatalog.Purge(root, session.Id, File.Delete);
            }
            return "Trash emptied.";
        });
    }

    private async void StorageToolsClick(object sender, RoutedEventArgs e)
    {
        if (_libraryBusy || _exporting || CaptureActive()) { ShowToast("Storage tools are available after your match."); return; }
        var root = _settings.OutputDirectory;
        LibraryUsage usage;
        try { usage = await Task.Run(() => LibraryCatalog.Usage(root)); }
        catch (Exception ex) { ShowToast($"Could not read storage: {ShortError(ex)}"); return; }
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"{Size(usage.ActiveBytes)} in recordings · {Size(usage.TrashBytes)} in Trash", Margin = new(0, 0, 0, 10) });
        var limit = new TextBox { Text = _settings.LibraryLimitMegabytes.ToString(), MaxLength = 8 };
        AddField(panel, "Recording limit in MiB (0 for unlimited)", limit);
        var auto = new CheckBox { Content = "Automatically clean up after matches", IsChecked = _settings.AutoManageLibrary, Margin = new(0, 12, 0, 0) };
        panel.Children.Add(auto);
        panel.Children.Add(new TextBlock { Text = "Cleanup permanently removes the oldest unprotected sessions. Favorited recordings and sessions with favorited clips are kept.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        if (!ShowToolDialog("Storage and cleanup", panel, "Apply")) return;
        if (!long.TryParse(limit.Text, out var megabytes)) { ShowToast("Enter a whole number in MiB."); return; }
        bool enable = auto.IsChecked == true;
        if (enable && !_settings.AutoManageLibrary && MessageBox.Show(this, "Automatic cleanup permanently deletes eligible old recordings when the limit is exceeded.", "Enable automatic cleanup",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        try
        {
            PersistToolSettings(_settings with { LibraryLimitMegabytes = megabytes, AutoManageLibrary = enable });
            _managedCatalog = null;
            if (enable) ScheduleStorageCleanup();
        }
        catch (Exception ex) { ShowToast(ShortError(ex)); }
    }

    private async void CleanToLimitClick()
    {
        if (CaptureActive() || _settings.LibraryLimitMegabytes <= 0) return;
        if (MessageBox.Show(this, "Permanently remove old unprotected recordings until the library meets its storage limit?", "Clean up storage",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var root = _settings.OutputDirectory;
        var limit = _settings.LibraryLimitMegabytes * 1024L * 1024;
        await RunLibraryJob(() =>
        {
            var count = LibraryCatalog.Manage(root, limit, File.Delete, StopMediaWork);
            var remaining = LibraryCatalog.Usage(root).ActiveBytes;
            return remaining > limit ? $"{Size(remaining)} remain. Available recordings are protected or still preparing."
                : $"Cleaned up {count} recording session(s). {Size(remaining)} remain.";
        });
    }

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GiB" : $"{bytes / (1024d * 1024):0.0} MiB";

    private async void ScheduleStorageCleanup()
    {
        if (!_startRecorder || _libraryBusy || _exporting || EditorPanel.IsVisible || !_settings.AutoManageLibrary || _settings.LibraryLimitMegabytes <= 0) return;
        if (_managedCatalog == _catalogSignature || CaptureActive()) return;
        _managedCatalog = _catalogSignature;
        var root = _settings.OutputDirectory;
        long limit = _settings.LibraryLimitMegabytes * 1024L * 1024;
        _libraryBusy = true;
        UpdateSelectionToolbar();
        try
        {
            int removed = await Task.Run(() => LibraryCatalog.Manage(root, limit, File.Delete, StopMediaWork));
            if (removed > 0) { _catalogSignature = ""; ShowToast($"Cleaned up {removed} old recording session(s)."); }
        }
        catch (Exception ex) { ShowToast($"Storage cleanup stopped: {ShortError(ex)}"); }
        finally { _libraryBusy = false; UpdateSelectionToolbar(); }
    }

    private void PersistToolSettings(EchoSettings settings)
    {
        settings.Validate();
        JsonStore.Write(_paths.Settings, settings);
        _settings = settings;
    }

    private static void AddField(Panel panel, string title, Control control)
    {
        var label = new Label { Content = title, Target = control, Padding = new(0), Margin = new(0, 10, 0, 6) };
        label.SetResourceReference(ForegroundProperty, "TextBrush");
        panel.Children.Add(label);
        panel.Children.Add(control);
    }

    private bool ShowToolDialog(string title, FrameworkElement content, string confirm, double width = 500, Func<bool>? validate = null)
    {
        var dialog = new Window { Owner = this, Title = title, Width = width, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height * .88), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, UseLayoutRounding = true };
        dialog.SetResourceReference(BackgroundProperty, "PanelBrush");
        dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        dialog.SetResourceReference(FontFamilyProperty, "StudioFont");
        var layout = new DockPanel { Margin = new(24, 18, 24, 20) };
        var heading = new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        var accept = new Button { Content = confirm, IsDefault = true };
        accept.SetResourceReference(StyleProperty, "AccentButton");
        accept.Click += (_, _) => { if (validate?.Invoke() != false) dialog.DialogResult = true; };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0) });
        buttons.Children.Add(accept);
        layout.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }
}
