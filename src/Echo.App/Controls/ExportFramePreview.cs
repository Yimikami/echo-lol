using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Echo.App;

internal sealed class ExportFramePreview : FrameworkElement
{
    public ImageSource? Frame { get; init; }
    public ExportOptions Options { get; set; } = new();
    public event Action<double, double>? Reposition;
    private Point _drag;
    private double _x, _y;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Frame == null || Frame.Width <= 0 || Frame.Height <= 0 || ActualWidth < 2 || ActualHeight < 2) return;
        var aspect = Options.Layout switch { ExportLayout.Original => Frame.Width / Frame.Height, ExportLayout.Square => 1, _ => 9d / 16 };
        var bounds = Fit(aspect, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle((Brush)FindResource("BackgroundBrush"), null, bounds);
        dc.PushClip(new RectangleGeometry(bounds));
        if (Options.Layout == ExportLayout.PortraitHud)
        {
            Rect Scale(double x, double y, double w, double h) => new(bounds.X + x * bounds.Width / 1080, bounds.Y + y * bounds.Height / 1920, w * bounds.Width / 1080, h * bounds.Height / 1920);
            dc.DrawImage(Frame, Fit(Frame.Width / Frame.Height, Scale(0, 0, 1080, 1370)));
            Tile(dc, new(Options.MinimapOnLeft ? 0 : .77, .64, .23, .36), Scale(685, 1500, 350, 350));
            Tile(dc, new(.27, .76, .46, .24), Scale(45, 1500, 610, 350));
        }
        else
        {
            var scale = Math.Max(bounds.Width / Frame.Width, bounds.Height / Frame.Height);
            var width = Frame.Width * scale;
            var height = Frame.Height * scale;
            dc.DrawImage(Frame, new Rect(bounds.X - (width - bounds.Width) * Options.Focus,
                bounds.Y - (height - bounds.Height) * Options.VerticalFocus, width, height));
        }
        dc.Pop();
        dc.DrawRectangle(null, new Pen((Brush)FindResource("LineBrush"), 1), bounds);
    }

    private void Tile(DrawingContext dc, Rect source, Rect available)
    {
        if (Frame == null) return;
        var bounds = Fit(Frame.Width * source.Width / (Frame.Height * source.Height), available);
        dc.PushClip(new RectangleGeometry(bounds));
        var width = bounds.Width / source.Width;
        var height = bounds.Height / source.Height;
        dc.DrawImage(Frame, new Rect(bounds.X - width * source.X, bounds.Y - height * source.Y, width, height));
        dc.Pop();
    }

    private static Rect Fit(double aspect, Rect available)
    {
        var width = Math.Min(available.Width, available.Height * aspect);
        var height = width / aspect;
        return new(available.X + (available.Width - width) / 2, available.Y + (available.Height - height) / 2, width, height);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Options.Layout is not (ExportLayout.Portrait or ExportLayout.Square)) return;
        _drag = e.GetPosition(this);
        _x = Options.Focus;
        _y = Options.VerticalFocus;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var delta = e.GetPosition(this) - _drag;
        Reposition?.Invoke(Math.Clamp(_x - delta.X / 200, 0, 1), Math.Clamp(_y - delta.Y / 200, 0, 1));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }
}
