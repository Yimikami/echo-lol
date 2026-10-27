using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Echo.Core;

namespace Echo.App.Controls;

/// <summary>A bounded, native timeline. No media decoding happens in its render path.</summary>
public sealed class ClipTimeline : FrameworkElement
{
    public double Duration { get; set; }
    public double SelectionStart { get; set; }
    public double SelectionEnd { get; set; }
    public double Position { get; set; }
    public IReadOnlyList<ImageSource> Frames { get; set; } = [];
    public IReadOnlyList<(double Time, MomentKind Kind)> Moments { get; set; } = [];
    public event Action<double>? SeekRequested;
    public event Action<double, double>? RangeChanged;
    public event Action<double>? MomentRequested;
    private int _drag;
    private const double Inset = 12, Top = 31;
    private double StripHeight => Math.Max(44, Math.Min(76, ActualHeight - 78));
    private double MarkerY => Top + StripHeight + 23;
    private double Span => Math.Max(1, ActualWidth - Inset * 2);
    private double X(double time) => Inset + (Duration > 0 ? Math.Clamp(time / Duration, 0, 1) : 0) * Span;
    private double At(double x) => Math.Clamp((x - Inset) / Span * Duration, 0, Duration);
    private static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static readonly Brush Accent = Brush("#C2D4FF"), Secondary = Brush("#A1A7B2"), Surface = Brush("#111318"), Dim = Brush("#B00C0D11");

    public ClipTimeline() { Focusable = true; Cursor = Cursors.Hand; MinHeight = 106; ToolTip = "Drag the edges to trim. Click a moment to preview its lead-in. Arrow keys seek; Shift + arrow seeks 0.1s."; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawRoundedRectangle(Surface, null, new Rect(Inset, Top, Span, StripHeight), 5, 5);
        if (Duration <= 0) { Text(dc, "Select a clip to edit its timeline", new Point(Inset + 12, Top + 15), Secondary); return; }
        for (int i = 0; i <= 5; i++)
        {
            var t = Duration * i / 5;
            var label = TimeSpan.FromSeconds(t).ToString(Duration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
            Text(dc, label, new Point(Math.Clamp(X(t) - 15, 0, Math.Max(0, ActualWidth - 42)), 4), Secondary);
        }
        if (Frames.Count > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(Inset, Top, Span, StripHeight), 5, 5));
            var cellWidth = Math.Max(80, StripHeight * 16 / 9);
            var cells = Math.Max(1, (int)Math.Ceiling(Span / cellWidth));
            var width = Span / cells;
            for (int i = 0; i < cells; i++)
            {
                var frame = Frames[Math.Min(Frames.Count - 1, (int)((i + .5) / cells * Frames.Count))];
                var rect = new Rect(Inset + i * width, Top, width + 1, StripHeight);
                var imageWidth = StripHeight * 16 / 9;
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawImage(frame, new Rect(rect.X + (rect.Width - imageWidth) / 2, Top, imageWidth, StripHeight));
                dc.Pop();
            }
            dc.Pop();
        }
        var left = X(SelectionStart); var right = X(SelectionEnd);
        dc.DrawRectangle(Dim, null, new Rect(Inset, Top, Math.Max(0, left - Inset), StripHeight));
        dc.DrawRectangle(Dim, null, new Rect(right, Top, Math.Max(0, Inset + Span - right), StripHeight));
        dc.DrawRoundedRectangle(null, new Pen(Accent, 2), new Rect(left, Top, Math.Max(1, right - left), StripHeight), 3, 3);
        foreach (var x in new[] { left, right })
        {
            dc.DrawRoundedRectangle(Accent, null, new Rect(x - 5, Top - 1, 10, StripHeight + 2), 3, 3);
            dc.DrawLine(new Pen(Surface, 1.5), new Point(x, Top + 17), new Point(x, Top + 29));
        }
        double lastLabelX = -100;
        foreach (var moment in Moments.Where(m => m.Time >= 0 && m.Time <= Duration))
        {
            var color = moment.Kind == MomentKind.Death ? Brush("#FFA891") : moment.Kind == MomentKind.Assist ? Brush("#A3C8ED") : Accent;
            dc.DrawRoundedRectangle(color, null, new Rect(X(moment.Time) - 2, MarkerY - 6, 4, 12), 2, 2);
            if (X(moment.Time) > lastLabelX + 92)
            {
                var kind = moment.Kind == MomentKind.Manual ? "Bookmark" : moment.Kind.ToString();
                if (TryFindResource("Phosphor." + kind) is Geometry icon)
                {
                    dc.PushTransform(new TranslateTransform(X(moment.Time) + 7, MarkerY - 7)); dc.PushTransform(new ScaleTransform(14d / 256, 14d / 256));
                    dc.DrawGeometry(color, null, icon); dc.Pop(); dc.Pop();
                }
                Text(dc, moment.Kind.ToString(), new Point(X(moment.Time) + 25, MarkerY - 8), color); lastLabelX = X(moment.Time);
            }
        }
        var play = X(Position);
        dc.DrawLine(new Pen(Brushes.White, 1.5), new Point(play, Top - 4), new Point(play, Top + StripHeight + 5));
        if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, new Pen(Accent, 1), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 4, 4);
    }
    private void Text(DrawingContext dc, string value, Point point, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("StudioFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (Duration <= 0) return;
        Focus(); var p = e.GetPosition(this);
        if (p.Y > Top + StripHeight + 7)
        {
            var candidates = Moments.Where(m => m.Time >= 0 && m.Time <= Duration).OrderBy(m => Math.Abs(X(m.Time) - p.X)).ToArray();
            if (candidates.Length > 0 && Math.Abs(X(candidates[0].Time) - p.X) < 14) { MomentRequested?.Invoke(candidates[0].Time); e.Handled = true; return; }
        }
        _drag = Math.Abs(p.X - X(SelectionStart)) < 11 ? 1 : Math.Abs(p.X - X(SelectionEnd)) < 11 ? 2 : 3;
        CaptureMouse(); Move(p.X); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Move(e.GetPosition(this).X); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) ReleaseMouseCapture(); _drag = 0; }
    private void Move(double x)
    {
        var t = At(x);
        if (_drag == 1) { SelectionStart = Math.Clamp(t, 0, Math.Max(0, SelectionEnd - .1)); RangeChanged?.Invoke(SelectionStart, SelectionEnd); SeekRequested?.Invoke(SelectionStart); }
        else if (_drag == 2) { SelectionEnd = Math.Clamp(t, Math.Min(Duration, SelectionStart + .1), Duration); RangeChanged?.Invoke(SelectionStart, SelectionEnd); SeekRequested?.Invoke(Math.Max(SelectionStart, SelectionEnd - .05)); }
        else SeekRequested?.Invoke(t);
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right) { SeekRequested?.Invoke(Math.Clamp(Position + (e.Key == Key.Right ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : 1), 0, Duration)); e.Handled = true; }
    }
}
