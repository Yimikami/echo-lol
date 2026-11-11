using System.IO;
using System.Windows;
using Echo.Core;
using Echo.Recorder;

namespace Echo.App;

public partial class MainWindow
{
    private Task? _placementBackgroundWork;

    internal Task LoadPlacementBackground(bool refresh = false)
    {
        if (_placementBackgroundWork is { IsCompleted: false }) return _placementBackgroundWork;
        return _placementBackgroundWork = LoadPlacementBackgroundCore(refresh);
    }

    private async Task LoadPlacementBackgroundCore(bool refresh)
    {
        var cached = Path.Combine(_paths.Root, "overlay-preview.png");
        // Keep the previous image throughout refresh, including capture deferral and failures.
        if (!InputPlacementPreview.HasBackground)
        {
            if (File.Exists(cached))
            {
                try { InputPlacementPreview.LoadBackground(cached); }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
                { InputPlacementPreview.LoadBackground(); }
            }
            else InputPlacementPreview.LoadBackground();
        }
        if (!refresh && InputPlacementPreview.BackgroundPath == cached) return;
        if (CaptureActive())
        {
            if (refresh) ShowToast("Refresh the preview after your match.");
            return;
        }

        var pending = Path.Combine(_paths.Root, Guid.NewGuid() + ".preview.png");
        PlacementRefreshButton.IsEnabled = false;
        PlacementRefreshButton.Content = "Loading…";
        try
        {
            var output = _settings.OutputDirectory;
            var source = await Task.Run(() => PlacementPreviewSource.Latest(LibraryCatalog.Read(output)));
            if (_closing) return;
            if (source == null)
            {
                if (refresh) ShowToast("No completed recording is available yet.");
                return;
            }
            await Task.Run(() => MediaTools.Thumbnail(source.Path, pending, source.At, 1600, () => _closing || CaptureActive()));
            if (_closing) return;
            // Decode before publishing the cache. Failed extraction never retires a good frame.
            var frame = Controls.OverlayPlacement.ReadBackground(pending);
            File.Move(pending, cached, true);
            InputPlacementPreview.SetBackground(frame, cached);
        }
        catch (OperationCanceledException)
        {
            if (refresh && !_closing) ShowToast("Refresh the preview after your match.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or
                                      UnauthorizedAccessException or NotSupportedException)
        {
            if (refresh && !_closing) ShowToast("Could not refresh the preview. Your previous image is still available.");
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
            PlacementRefreshButton.Content = "Use latest clip";
            PlacementRefreshButton.IsEnabled = true;
        }
    }

    private async void RefreshPlacementPreviewClick(object sender, RoutedEventArgs e) => await LoadPlacementBackground(true);
}
