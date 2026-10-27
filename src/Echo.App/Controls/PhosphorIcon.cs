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
        if (TryFindResource("Phosphor." + Kind) is not Geometry geometry) return;
        var size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 256, size / 256));
        dc.DrawGeometry(Foreground, null, geometry);
        dc.Pop(); dc.Pop();
    }
}
