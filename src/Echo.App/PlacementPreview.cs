using System.IO;
using System.Windows;
using Echo.Recorder;

namespace Echo.App;
public partial class MainWindow
{
    private Task? _placementBackgroundWork;
    internal Task LoadPlacementBackground(bool refresh=false)
    {
        if(_placementBackgroundWork is {IsCompleted:false})return _placementBackgroundWork;
        return _placementBackgroundWork=LoadPlacementBackgroundCore(refresh);
    }
    private async Task LoadPlacementBackgroundCore(bool refresh)
    {
        var cached=Path.Combine(_paths.Root,"overlay-preview.png");
        if(!refresh&&File.Exists(cached)){InputPlacementPreview.LoadBackground(cached);return;}
        InputPlacementPreview.LoadBackground();
        var source=_selected?.IsReady==true?_selected:_items.FirstOrDefault(i=>i.IsReady);
        if(source==null||CaptureActive())return;
        var pending=Path.Combine(_paths.Root,Guid.NewGuid()+".preview.png");
        try
        {
            var at=source.Clip.Start+Math.Min(3,(source.Clip.End-source.Clip.Start)/2);
            await Task.Run(()=>MediaTools.Thumbnail(source.Clip.Path,pending,at,1280));
            File.Move(pending,cached,true);
            if(!_closing)InputPlacementPreview.LoadBackground(cached);
        }
        catch(Exception ex)when(ex is IOException or InvalidOperationException or TimeoutException){if(refresh&&!_closing)ShowToast("Could not load the recording preview.");}
        finally{if(File.Exists(pending))File.Delete(pending);}
    }
    private async void RefreshPlacementPreviewClick(object sender,RoutedEventArgs e)=>await LoadPlacementBackground(true);
}
