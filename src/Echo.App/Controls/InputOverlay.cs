using System.Windows;
using System.Windows.Media;
using Echo.Core;
namespace Echo.App.Controls;
public sealed class InputOverlay : FrameworkElement
{
    public HashSet<string> Held { get; set; } = [];
    public int Apm { get; set; }
    public bool HasData { get; set; } = true;
    public InputOverlayPreset Preset { get; set; } = new();
    public double Aspect => (double)OverlayAppearance.Layout(Preset.Layout).Width / (Preset.Collapsed ? 38 : OverlayAppearance.Layout(Preset.Layout).Height);
    private static SolidColorBrush Brush(string rgb, byte alpha=255)
    {
        var brush=new SolidColorBrush(Color.FromArgb(alpha,Convert.ToByte(rgb[..2],16),Convert.ToByte(rgb[2..4],16),Convert.ToByte(rgb[4..6],16)));brush.Freeze();return brush;
    }
    protected override void OnRender(DrawingContext dc)
    {
        // Transparent hit area keeps a wireframe overlay draggable between the keys.
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        var art=OverlayArtwork.Get(Preset.Layout);var colors=OverlayAppearance.Colors(Preset.Style);var scale=ActualWidth/art.Width;
        var stroke=Brush(colors.Line);var label=Brush(colors.Text);var pressed=Brush(colors.Pressed);var activeLabel=Brush(colors.Key);var idle=Brush(colors.Key,Preset.Style=="Light"?(byte)223:(byte)159);
        dc.PushTransform(new ScaleTransform(scale,scale));
        if(!Preset.Collapsed)foreach(var part in art.Parts)
        {
            var down=part.Control!=null&&Held.Contains(part.Control);
            if(part.Pulse&&!down)continue;
            if(down)dc.DrawGeometry(pressed,null,part.Fill);
            else if(Preset.Style!="Outline")dc.DrawGeometry(idle,null,part.Fill);
            dc.DrawGeometry(stroke,null,part.Outline);
            if(part.Label!=null)dc.DrawGeometry(down?activeLabel:label,null,part.Label);
        }
        if(Preset.ShowApm||Preset.Collapsed)dc.DrawGeometry(label,null,OverlayArtwork.Header(Preset.ShowApm?HasData?$"{Apm} APM":"— APM":"INPUTS"));
        dc.Pop();
    }
}