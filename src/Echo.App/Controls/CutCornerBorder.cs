using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Echo.App.Controls;

public sealed class CutCornerBorder : Border
{
    public static readonly DependencyProperty CutProperty = DependencyProperty.Register(
        nameof(Cut), typeof(double), typeof(CutCornerBorder), new PropertyMetadata(16d, (target, _) => ((CutCornerBorder)target).UpdateClip()));

    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    private void UpdateClip()
    {
        var size = RenderSize;
        var cut = Math.Clamp(Cut, 0, Math.Min(size.Width, size.Height));
        if (cut == 0)
        {
            Clip = null;
            return;
        }

        var clip = new StreamGeometry();
        using (var drawing = clip.Open())
        {
            drawing.BeginFigure(new(0, 0), true, true);
            drawing.PolyLineTo([new(size.Width - cut, 0), new(size.Width, cut), new(size.Width, size.Height), new(0, size.Height)], true, false);
        }
        clip.Freeze();
        Clip = clip;
    }
}
