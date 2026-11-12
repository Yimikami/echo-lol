using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Echo.Core;
namespace Echo.App;
public partial class MainWindow
{
    private List<InputSample> _inputSamples = [];
    private GameplayInputPlayback _inputPlayback = new([]);
    private bool _hasInputData;
    private readonly System.Windows.Threading.DispatcherTimer _inputTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private bool _overlayReady,_inputDragging;
    private Point _inputGrab;
    private InputOverlayPreset _inputPreset=new();
    private void LoadOverlayFields()
    {
        _inputPreset=_settings.InputOverlay;InputVisibleBox.IsChecked=_inputPreset.Enabled;InputApmBox.IsChecked=_inputPreset.ShowApm;
        InputLayoutBox.SelectedIndex=_inputPreset.Layout=="Full"?1:_inputPreset.Layout=="Mouse"?2:_inputPreset.Layout=="Custom"?3:0;InputStyleBox.SelectedIndex=_inputPreset.Style=="Light"?1:_inputPreset.Style=="Outline"?2:0;InputSizeSlider.Value=_inputPreset.Width;
        ThemeBox.SelectedIndex=_settings.Theme=="Light"?1:_settings.Theme=="System"?2:0;
        RefreshOverlayPreview();
    }
    private InputOverlayPreset ReadOverlayFields()=>_inputPreset with{Enabled=InputVisibleBox.IsChecked==true,ShowApm=InputApmBox.IsChecked==true,Layout=InputLayoutBox.SelectedIndex==1?"Full":InputLayoutBox.SelectedIndex==2?"Mouse":InputLayoutBox.SelectedIndex==3?"Custom":"Compact",Style=InputStyleBox.SelectedIndex==1?"Light":InputStyleBox.SelectedIndex==2?"Outline":"Dark",Width=InputSizeSlider.Value};
    private void RefreshOverlayPreview()
    {
        InputPlacementPreview.SetPreset(_inputPreset);
        var updating=_updating;_updating=true;
        var position = Enumerable.Range(0, 4).FirstOrDefault(index =>
        {
            var corner = OverlayCorner(index);
            return Math.Abs(_inputPreset.X - corner.X) < .001 && Math.Abs(_inputPreset.Y - corner.Y) < .001;
        }, -1);
        InputCornerChoice.SelectedIndex = position switch { 2 => 0, 3 => 1, 0 => 2, 1 => 3, _ => -1 };
        InputPositionLabel.Text = position switch { 0 => "Bottom left", 1 => "Bottom right", 2 => "Top left", 3 => "Top right", _ => "Custom" };
        InputSizeValue.Text = $"{Math.Round(_inputPreset.Width * 100):0}%";
        _updating=updating;
    }
    private void LoadInputData(ClipItem item)
    {
        _inputSamples = GameplayInputs.Read(item.Session.InputLogPath);
        _inputPlayback = new(_inputSamples);
        _hasInputData = File.Exists(item.Session.InputLogPath);
        PlaybackActivity.Activity = _hasInputData ? InputActivity.Build(_inputSamples, item.Clip, item.Clip.Start, item.Clip.End) : [];
        UpdatePlaybackInputs(item.Clip.Start);
    }
    private Rect VideoBounds()
    {
        var w=InputCanvas.ActualWidth;var h=InputCanvas.ActualHeight;var nw=Player.NaturalVideoWidth>0?Player.NaturalVideoWidth:_selected?.Session.RecordingOptions?.Width??1920;var nh=Player.NaturalVideoHeight>0?Player.NaturalVideoHeight:_selected?.Session.RecordingOptions?.Height??1080;
        var scale=Math.Min(w/nw,h/nh);return new((w-nw*scale)/2,(h-nh*scale)/2,nw*scale,nh*scale);
    }
    private void UpdatePlaybackInputs(double position)
    {
        if(!_overlayReady||PlaybackInputs==null)return;
        PlaybackInputs.Preset=_inputPreset;
        PlaybackInputs.Visibility=_selected!=null&&_inputPreset.Enabled?Visibility.Visible:Visibility.Collapsed;
        RefreshInputTimer();
        UpdateInputState(position);
        var frame=VideoBounds();var maxHeight=frame.Height;var width=Math.Min(frame.Width*_inputPreset.Width,maxHeight*PlaybackInputs.Aspect);var height=width/PlaybackInputs.Aspect;
        PlaybackInputs.Width=InputLayerOutline.Width=width;PlaybackInputs.Height=InputLayerOutline.Height=height;
        var x=frame.X+Math.Clamp(_inputPreset.X*frame.Width,0,Math.Max(0,frame.Width-width));var y=frame.Y+Math.Clamp(_inputPreset.Y*frame.Height,0,Math.Max(0,frame.Height-height));
        foreach(var element in new FrameworkElement[]{PlaybackInputs,InputLayerOutline}){Canvas.SetLeft(element,x);Canvas.SetTop(element,y);}
        Canvas.SetLeft(InputResizeGrip,x+width-8);Canvas.SetTop(InputResizeGrip,y+height-8);Canvas.SetLeft(InputCollapseButton,x+width-28);Canvas.SetTop(InputCollapseButton,y+6);
        InputCollapseButton.Content=_inputPreset.Collapsed?"+":"−";PlaybackInputs.InvalidateVisual();
        if(PlaybackInputs.Visibility!=Visibility.Visible)ShowInputChrome(false);
    }
    private void RefreshInputTimer()
    {
        _inputTimer.IsEnabled = _playing && !_seeking && _selected != null && _hasInputData && _inputPreset.Enabled
            && !_inputPreset.Collapsed && WindowState != WindowState.Minimized;
    }
    private void UpdateInputState(double position)
    {
        if (_selected == null || !_inputPreset.Enabled) return;
        var at = GameplayInputs.RecordingTime(_selected.Clip, position);
        HashSet<string> held = _inputPreset.Collapsed ? [] : _inputPlayback.Visible(at);
        if (_inputPreset.Layout is "Compact" or "Full")
        {
            if (held.Contains("RightShift")) held.Add("Shift");
            if (held.Contains("RightCtrl")) held.Add("Ctrl");
            if (held.Contains("RightAlt")) held.Add("Alt");
        }
        int apm = _inputPreset.ShowApm ? _inputPlayback.Apm(at) : 0;
        if (PlaybackInputs.HasData == _hasInputData && PlaybackInputs.Apm == apm && PlaybackInputs.Held.SetEquals(held)) return;
        PlaybackInputs.HasData = _hasInputData;
        PlaybackInputs.Held = held;
        PlaybackInputs.Apm = apm;
        PlaybackInputs.InvalidateVisual();
    }
    private void ShowInputChrome(bool show){var state=show&&PlaybackInputs.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;InputLayerOutline.Visibility=InputResizeGrip.Visibility=InputCollapseButton.Visibility=state;}
    private void InputCanvasHover(object sender,MouseEventArgs e){if(_inputDragging||InputResizeGrip.IsDragging)return;var p=e.GetPosition(InputCanvas);var rect=new Rect(Canvas.GetLeft(PlaybackInputs)-8,Canvas.GetTop(PlaybackInputs)-8,Math.Max(1,PlaybackInputs.ActualWidth+16),Math.Max(1,PlaybackInputs.ActualHeight+16));ShowInputChrome(rect.Contains(p));}
    private void InputCanvasLeave(object sender,MouseEventArgs e){if(!_inputDragging&&!InputResizeGrip.IsDragging)ShowInputChrome(false);}
    private void InputCanvasResized(object sender,SizeChangedEventArgs e)=>UpdatePlaybackInputs(Player?.Position.TotalSeconds??0);
    private void InputLayerDown(object sender,MouseButtonEventArgs e){_inputDragging=true;_inputGrab=e.GetPosition(PlaybackInputs);PlaybackInputs.CaptureMouse();ShowInputChrome(true);e.Handled=true;}
    internal void MoveInputLayer(Point point)
    {
        var frame=VideoBounds();if(frame.Width<=0||frame.Height<=0)return;
        _inputPreset=_inputPreset with{X=Math.Clamp(point.X-_inputGrab.X-frame.X,0,Math.Max(0,frame.Width-PlaybackInputs.Width))/frame.Width,Y=Math.Clamp(point.Y-_inputGrab.Y-frame.Y,0,Math.Max(0,frame.Height-PlaybackInputs.Height))/frame.Height};UpdatePlaybackInputs(Player.Position.TotalSeconds);
    }
    private void InputLayerMove(object sender,MouseEventArgs e){if(_inputDragging&&PlaybackInputs.IsMouseCaptured)MoveInputLayer(e.GetPosition(InputCanvas));}
    private void InputLayerUp(object sender,MouseButtonEventArgs e){_inputDragging=false;PlaybackInputs.ReleaseMouseCapture();PersistInputPreset();e.Handled=true;}
    private void InputResizeStarted(object sender,DragStartedEventArgs e)=>ShowInputChrome(true);
    private void InputResizeDelta(object sender,DragDeltaEventArgs e){var frame=VideoBounds();if(frame.Width<=0)return;_inputPreset=_inputPreset with{Width=Math.Clamp(_inputPreset.Width+e.HorizontalChange/frame.Width,.15,.85)};UpdatePlaybackInputs(Player.Position.TotalSeconds);}
    private void InputResizeCompleted(object sender,DragCompletedEventArgs e)=>PersistInputPreset();
    private void CollapsePlaybackInputs(object sender,RoutedEventArgs e){_inputPreset=_inputPreset with{Collapsed=!_inputPreset.Collapsed};PersistInputPreset();}
    private void TogglePlaybackInputs(object sender,RoutedEventArgs e){_inputPreset=_inputPreset with{Enabled=!_inputPreset.Enabled};PersistInputPreset();}
    private void PersistInputPreset()
    {
        try{_inputPreset.Validate();_settings=_settings with{InputOverlay=_inputPreset};JsonStore.Write(_paths.Settings,_settings);_updating=true;LoadOverlayFields();_updating=false;UpdatePlaybackInputs(Player.Position.TotalSeconds);}
        catch(Exception ex){_updating=false;ShowError("Couldn't save overlay", ex);}
    }
    private void InputSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InputSizeValue != null) InputSizeValue.Text = $"{Math.Round(e.NewValue * 100):0}%";
        if (_overlayReady && !_updating) AutoSave(false);
    }

    private void InputCornerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_overlayReady || _updating || InputCornerChoice.SelectedItem is not ListBoxItem item) return;
        var point = OverlayCorner(int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture));
        _inputPreset = _inputPreset with { X = point.X, Y = point.Y };
        PersistInputPreset();
    }

    private void InputSettingsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InputOptionsColumn != null) InputOptionsColumn.Width = new GridLength(e.NewSize.Width >= 1100 ? 320 : 280);
    }

    private Point OverlayCorner(int index)
    {
        var layout=OverlayAppearance.Layout(_inputPreset);var aspect=layout.Width/(double)(_inputPreset.Collapsed?38:layout.Height);
        var width=Math.Min(_inputPreset.Width,aspect*9/16);var height=width/aspect*16/9;
        return new(index is 1 or 3?Math.Max(0,.98-width):.02,index<2?Math.Max(0,.98-height):.02);
    }
    private void RunSetupClick(object sender,RoutedEventArgs e){new OnboardingWindow(_paths){Owner=this}.ShowDialog();_settings=_paths.LoadSettings();_updating=true;LoadSettingsFields();_updating=false;UpdatePlaybackInputs(Player.Position.TotalSeconds);}
    private void CustomizeInputsClick(object sender,RoutedEventArgs e)
    {
        new InputKeysWindow(_inputPreset,preset=>{_inputPreset=preset;PersistInputPreset();}){Owner=this}.ShowDialog();
    }
}
