using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Echo.Core;

namespace Echo.App.Controls;

/// <summary>Read-only activity and event navigation, independent of the optional trim workspace.</summary>
public sealed class PlaybackTimeline : FrameworkElement
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Position { get; set; }
    public IReadOnlyList<ActivityPoint> Activity { get; set; }=[];
    public IReadOnlyList<(double Time,MomentKind Kind)> Moments { get; set; }=[];
    public event Action<double>? SeekRequested;
    public event Action<double>? MomentRequested;
    private const double Left=32,Right=16,GraphTop=6,GraphBottom=52,MarkerY=70;
    private double Span=>Math.Max(1,ActualWidth-Left-Right);
    private double X(double time)=>Left+Math.Clamp((time-Start)/Math.Max(.001,End-Start),0,1)*Span;
    private double TimeAt(double x)=>Start+Math.Clamp((x-Left)/Span,0,1)*Math.Max(0,End-Start);
    private static readonly Brush Death=Frozen("#D27485"),Assist=Frozen("#55A98A");
    private DrawingGroup? _chart;
    private IReadOnlyList<ActivityPoint>? _cachedActivity;
    private IReadOnlyList<(double Time,MomentKind Kind)>? _cachedMoments;
    private double _cachedStart,_cachedEnd;
    private Size _cachedSize;
    private Brush? _cachedTheme;
    private static SolidColorBrush Frozen(string color){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));brush.Freeze();return brush;}
    public PlaybackTimeline(){Focusable=true;Cursor=Cursors.Hand;MinHeight=98;}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));if(End<=Start)return;
        var theme=(Brush)FindResource("TextBrush");
        if(_chart==null||_cachedActivity!=Activity||_cachedMoments!=Moments||_cachedStart!=Start||_cachedEnd!=End||_cachedSize!=RenderSize||_cachedTheme!=theme)
        {
            var chart=new DrawingGroup();using(var drawing=chart.Open())DrawChart(drawing);chart.Freeze();_chart=chart;
            _cachedActivity=Activity;_cachedMoments=Moments;_cachedStart=Start;_cachedEnd=End;_cachedSize=RenderSize;_cachedTheme=theme;
        }
        dc.DrawDrawing(_chart);
        var play=X(Position);dc.DrawLine(new Pen(theme,1.4),new(play,GraphTop),new(play,MarkerY+11));dc.DrawEllipse(theme,null,new(play,GraphTop),3,3);
    }
    private void DrawChart(DrawingContext dc)
    {
        var Accent=(SolidColorBrush)FindResource("AccentBrush");var Grid=(Brush)FindResource("LineBrush");var Muted=(Brush)FindResource("SecondaryBrush");
        var accentColor=Accent.Color;var Fill=new SolidColorBrush(Color.FromArgb(36,accentColor.R,accentColor.G,accentColor.B));Fill.Freeze();
        var points=Activity.Where(p=>p.Time>=Start&&p.Time<=End).ToArray();var peak=Math.Max(1,points.Select(p=>p.Apm).DefaultIfEmpty().Max());
        foreach(var fraction in new[]{0d,.5,1d})dc.DrawLine(new Pen(Grid,.7),new(Left,GraphTop+(GraphBottom-GraphTop)*fraction),new(ActualWidth-Right,GraphTop+(GraphBottom-GraphTop)*fraction));
        if(points.Length>0)
        {
            var shape=new StreamGeometry();using(var path=shape.Open()){path.BeginFigure(new(X(points[0].Time),GraphBottom),true,true);foreach(var point in points)path.LineTo(new(X(point.Time),GraphBottom-point.Apm/(double)peak*(GraphBottom-GraphTop)),true,false);path.LineTo(new(X(points[^1].Time),GraphBottom),true,false);}shape.Freeze();
            dc.DrawGeometry(Fill,null,shape);
            var line=new StreamGeometry();using(var path=line.Open()){path.BeginFigure(new(X(points[0].Time),GraphBottom-points[0].Apm/(double)peak*(GraphBottom-GraphTop)),false,false);foreach(var point in points.Skip(1))path.LineTo(new(X(point.Time),GraphBottom-point.Apm/(double)peak*(GraphBottom-GraphTop)),true,false);}line.Freeze();dc.DrawGeometry(null,new Pen(Accent,1.6),line);
            Text(dc,peak.ToString(CultureInfo.InvariantCulture),new(0,GraphTop-3),Muted);Text(dc,"0",new(12,GraphBottom-10),Muted);
        }
        else Text(dc,"No input data",new(Left+8,GraphTop+13),Muted);
        foreach(var moment in Moments.Where(m=>m.Time>=Start&&m.Time<=End))
        {
            var color=moment.Kind==MomentKind.Death?Death:moment.Kind==MomentKind.Assist?Assist:Accent;var x=X(moment.Time);
            dc.DrawLine(new Pen(color,1),new(x,GraphBottom+4),new(x,MarkerY-9));
            dc.DrawEllipse((Brush)FindResource("BackgroundBrush"),new Pen(color,1),new(x,MarkerY),9,9);
            var kind=moment.Kind==MomentKind.Manual?"Bookmark":moment.Kind.ToString();
            if(TryFindResource("Phosphor."+kind) is Geometry icon){dc.PushTransform(new TranslateTransform(x-6,MarkerY-6));dc.PushTransform(new ScaleTransform(12d/256,12d/256));dc.DrawGeometry(color,null,icon);dc.Pop();dc.Pop();}
        }
        for(int i=0;i<=5;i++){var time=Start+(End-Start)*i/5;Text(dc,Stamp(time-Start),new(Math.Clamp(X(time)-15,0,Math.Max(0,ActualWidth-38)),88),Muted);}
    }
    private static string Stamp(double value)=>TimeSpan.FromSeconds(Math.Max(0,value)).ToString(value>=3600?@"hh\:mm\:ss":@"mm\:ss");
    private void Text(DrawingContext dc,string text,Point point,Brush brush)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface((FontFamily)FindResource("StudioFont"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),10,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);if(End<=Start)return;Focus();var at=e.GetPosition(this);
        var markers=Moments.Where(m=>m.Time>=Start&&m.Time<=End).OrderBy(m=>Math.Abs(X(m.Time)-at.X)).ToArray();
        if(Math.Abs(at.Y-MarkerY)<14&&markers.Length>0&&Math.Abs(X(markers[0].Time)-at.X)<11)MomentRequested?.Invoke(markers[0].Time);
        else{CaptureMouse();SeekRequested?.Invoke(TimeAt(at.X));}e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);var point=e.GetPosition(this);if(IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed){SeekRequested?.Invoke(TimeAt(point.X));return;}
        var marker=Moments.Where(m=>m.Time>=Start&&m.Time<=End).OrderBy(m=>Math.Abs(X(m.Time)-point.X)).ToArray();var time=TimeAt(point.X);
        var activity=Activity.MinBy(p=>Math.Abs(p.Time-time));
        ToolTip=marker.Length>0&&Math.Abs(X(marker[0].Time)-point.X)<11?$"{marker[0].Kind} · {Stamp(marker[0].Time-Start)}":$"{Stamp(time-Start)}"+(activity!=null?$" · {activity.Apm} APM":"");
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);if(IsMouseCaptured)ReleaseMouseCapture();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key is Key.Left or Key.Right){SeekRequested?.Invoke(Math.Clamp(Position+(e.Key==Key.Right?1:-1)*(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:1),Start,Math.Max(Start,End)));e.Handled=true;}}
    internal void SeekFraction(double value)=>SeekRequested?.Invoke(Start+Math.Clamp(value,0,1)*Math.Max(0,End-Start));
}
