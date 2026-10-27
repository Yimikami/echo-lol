using System.Globalization;
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
    private static SolidColorBrush Brush(string rgb, byte alpha=255) => new(Color.FromArgb(alpha,Convert.ToByte(rgb[..2],16),Convert.ToByte(rgb[2..4],16),Convert.ToByte(rgb[4..6],16)));
    protected override void OnRender(DrawingContext dc)
    {
        var layout=OverlayAppearance.Layout(Preset.Layout); var colors=OverlayAppearance.Colors(Preset.Style); var scale=ActualWidth/layout.Width;
        dc.PushTransform(new ScaleTransform(scale,scale));
        dc.DrawRoundedRectangle(Brush(colors.Panel, Preset.Style=="Outline"?(byte)0:(byte)230),null,new Rect(0,0,layout.Width,Preset.Collapsed?38:layout.Height),9,9);
        void Text(string text,double x,double y,double size,double? width=null)
        {
            var ft=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Arial"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,Brush(colors.Text),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft,new Point(x+(width.HasValue?Math.Max(0,(width.Value-ft.Width)/2):0),y));
        }
        Text(Preset.ShowApm?HasData?$"{Apm} APM":"— APM":"INPUTS",12,10,15);
        if(!Preset.Collapsed)foreach(var key in layout.Keys)
        {
            var rect=new Rect(key.X,key.Y,key.Width,key.Height);
            dc.DrawRoundedRectangle(Brush(Held.Contains(key.Id)?colors.Pressed:colors.Key,Preset.Style=="Outline"&&!Held.Contains(key.Id)?(byte)90:(byte)255),new Pen(Brush(colors.Line),1),rect,4,4);
            Text(key.Label,key.X,key.Y+key.Height/2-7,key.Label.Length>2?10:14,key.Width);
        }
        dc.Pop();
    }
}