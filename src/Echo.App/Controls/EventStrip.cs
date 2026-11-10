using System.Windows;
using System.Windows.Media;
using Echo.Core;

namespace Echo.App.Controls;

public readonly record struct EventPoint(double Fraction, MomentKind Kind);

public sealed class EventStrip : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<EventPoint>), typeof(EventStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<EventPoint>? Points
    {
        get => (IReadOnlyList<EventPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (ActualWidth < 4 || ActualHeight < 2) return;
        double height = Math.Min(4, ActualHeight);
        double y = (ActualHeight - height) / 2;
        var track = (Brush)FindResource("StripTrackBrush");
        drawing.DrawRoundedRectangle(track, null, new Rect(0, y, ActualWidth, height), 2, 2);
        if (Points == null) return;
        foreach (var point in Points)
        {
            var brush = (Brush)FindResource(MomentGlyph.BrushFor(point.Kind));
            var x = Math.Clamp(point.Fraction, 0, 1) * (ActualWidth - 4);
            drawing.DrawRoundedRectangle(brush, null, new Rect(x, 0, 4, ActualHeight), 1, 1);
        }
    }
}
