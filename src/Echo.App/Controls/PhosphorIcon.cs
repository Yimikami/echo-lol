using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Echo.App.Controls;

/// <summary>Native rendering of the unmodified path data from Phosphor's regular icon family.</summary>
public sealed class PhosphorIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(PhosphorIcon), new FrameworkPropertyMetadata("Play", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public PhosphorIcon() { Width = 20; Height = 20; IsHitTestVisible = false; Focusable = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (TryFindResource("Reference." + Kind) is Geometry outline || TryFindResource("ReferenceFill." + Kind) is Geometry)
        {
            var bounds = Math.Min(ActualWidth, ActualHeight);
            dc.PushTransform(new TranslateTransform((ActualWidth - bounds) / 2, (ActualHeight - bounds) / 2));
            dc.PushTransform(new ScaleTransform(bounds / 24, bounds / 24));
            var pen = new Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (TryFindResource("Reference." + Kind) is Geometry stroke) dc.DrawGeometry(null, pen, stroke);
            if (TryFindResource("ReferenceFill." + Kind) is Geometry fill) dc.DrawGeometry(Foreground, null, fill);
            if (Kind == "Settings")
            {
                pen.DashStyle = new DashStyle([1.72, 1.33], 0);
                dc.DrawEllipse(null, pen, new Point(12, 12), 8.5, 8.5);
            }
            dc.Pop(); dc.Pop();
            return;
        }
        if (TryFindResource("Phosphor." + Kind) is not Geometry geometry) return;
        var size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 256, size / 256));
        dc.DrawGeometry(Foreground, null, geometry);
        dc.Pop(); dc.Pop();
    }
}
