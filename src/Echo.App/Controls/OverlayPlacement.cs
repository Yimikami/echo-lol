using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Echo.Core;

namespace Echo.App.Controls;

/// <summary>Settings-only game preview; normalised coordinates are shared with playback and export.</summary>
public sealed class OverlayPlacement : Grid
{
    private readonly Image _background = new() { Stretch=Stretch.Fill, IsHitTestVisible=false };
    private readonly Canvas _canvas = new() { ClipToBounds=true };
    private readonly InputOverlay _overlay = new() { Cursor=Cursors.SizeAll,HasData=true,Apm=120,Held=["Q","MouseRight"] };
    private readonly Border _outline = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly Thumb _resize = new() { Width=14,Height=14,Cursor=Cursors.SizeNWSE };
    private InputOverlayPreset _preset = new(); private Point _grab; private bool _dragging;
    public event Action<InputOverlayPreset>? PlacementChanged;
    public OverlayPlacement()
    {
        Focusable = true;
        _outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        GotKeyboardFocus += (_, _) => _outline.BorderThickness = new Thickness(2);
        LostKeyboardFocus += (_, _) => _outline.BorderThickness = new Thickness(1);
        ClipToBounds=true;Background=Brushes.Black;Children.Add(_background);Children.Add(_canvas);
        _canvas.Children.Add(_overlay);_canvas.Children.Add(_outline);_canvas.Children.Add(_resize);
        var border=new FrameworkElementFactory(typeof(Border));border.SetResourceReference(Border.BackgroundProperty,"AccentBrush");border.SetValue(Border.BorderBrushProperty,Brushes.Black);border.SetValue(Border.BorderThicknessProperty,new Thickness(2));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));_resize.Template=new ControlTemplate(typeof(Thumb)){VisualTree=border};
        SizeChanged+=(_,_)=>ArrangeOverlay();
        _overlay.MouseLeftButtonDown+=(_,e)=>{Focus();_grab=e.GetPosition(_overlay);_dragging=true;_overlay.CaptureMouse();e.Handled=true;};
        _overlay.MouseMove+=(_,e)=>{if(_dragging&&_overlay.IsMouseCaptured)MoveTo(e.GetPosition(_canvas));};
        _overlay.MouseLeftButtonUp+=(_,e)=>{_dragging=false;_overlay.ReleaseMouseCapture();PlacementChanged?.Invoke(_preset);e.Handled=true;};
        _overlay.LostMouseCapture+=(_,_)=>{if(_dragging){_dragging=false;PlacementChanged?.Invoke(_preset);}};
        _resize.DragDelta+=(_,e)=>ResizeBy(e.HorizontalChange);
        _resize.DragCompleted+=(_,_)=>PlacementChanged?.Invoke(_preset);
        ToolTip="Drag the overlay or its corner to position and resize it.";
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .01 : .002;
        Nudge(e.Key is Key.Left ? -amount : e.Key is Key.Right ? amount : 0,
            e.Key is Key.Up ? -amount : e.Key is Key.Down ? amount : 0);
        e.Handled = true;
    }

    internal void Nudge(double dx, double dy)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _preset = _preset with
        {
            X = Math.Clamp(_preset.X + dx, 0, Math.Max(0, 1 - _overlay.Width / ActualWidth)),
            Y = Math.Clamp(_preset.Y + dy, 0, Math.Max(0, 1 - _overlay.Height / ActualHeight))
        };
        ArrangeOverlay();
        PlacementChanged?.Invoke(_preset);
    }

    public void SetPreset(InputOverlayPreset preset){_preset=preset;ArrangeOverlay();}
    private string? _backgroundPath;
    internal bool HasBackground => _background.Source != null;
    internal BitmapSource? BackgroundImage => _background.Source as BitmapSource;
    internal string? BackgroundPath => _backgroundPath;

    internal void LoadBackground(string? path = null)
    {
        if (HasBackground && _backgroundPath == path) return;
        SetBackground(ReadBackground(path), path);
    }

    internal static BitmapSource ReadBackground(string? path)
    {
        using var stream = path == null ? null : File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.DecodePixelWidth = 1600;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (stream != null) bitmap.StreamSource = stream;
        else bitmap.UriSource = new Uri("pack://application:,,,/Echo;component/Assets/game-preview.png");
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal void SetBackground(BitmapSource image, string? path)
    {
        _background.Source = image;
        _backgroundPath = path;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsFinite(availableSize.Width) || availableSize.Width <= 0)
            return base.MeasureOverride(availableSize);
        var frame = new Size(availableSize.Width, availableSize.Width * 9 / 16);
        base.MeasureOverride(frame);
        return frame;
    }
    private void ArrangeOverlay()
    {
        if(ActualWidth<=0||ActualHeight<=0)return;
        _overlay.Preset=_preset with{Enabled=true};
        var width=Math.Min(ActualWidth*_preset.Width,ActualHeight*_overlay.Aspect);var height=width/_overlay.Aspect;
        _overlay.Width=_outline.Width=width;_overlay.Height=_outline.Height=height;
        var x=Math.Clamp(_preset.X*ActualWidth,0,Math.Max(0,ActualWidth-width));var y=Math.Clamp(_preset.Y*ActualHeight,0,Math.Max(0,ActualHeight-height));
        foreach(var item in new FrameworkElement[]{_overlay,_outline}){Canvas.SetLeft(item,x);Canvas.SetTop(item,y);}
        Canvas.SetLeft(_resize,x+width-8);Canvas.SetTop(_resize,y+height-8);_overlay.InvalidateVisual();
    }
    internal void MoveTo(Point point)
    {
        if(ActualWidth<=0||ActualHeight<=0)return;
        _preset=_preset with{X=Math.Clamp(point.X-_grab.X,0,Math.Max(0,ActualWidth-_overlay.Width))/ActualWidth,Y=Math.Clamp(point.Y-_grab.Y,0,Math.Max(0,ActualHeight-_overlay.Height))/ActualHeight};ArrangeOverlay();
    }
    internal void ResizeBy(double delta){if(ActualWidth<=0)return;_preset=_preset with{Width=Math.Clamp(_preset.Width+delta/ActualWidth,.15,.85)};ArrangeOverlay();}
    internal InputOverlayPreset Preset=>_preset;
    internal void Commit()=>PlacementChanged?.Invoke(_preset);
}
