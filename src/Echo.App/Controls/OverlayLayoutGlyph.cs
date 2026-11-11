using System.Windows;
using System.Windows.Media;

namespace Echo.App.Controls;

/// <summary>A small layout diagram; never part of the exported overlay.</summary>
public sealed class OverlayLayoutGlyph : FrameworkElement
{
    public string Kind { get; set; } = "Compact";

    protected override void OnRender(DrawingContext dc)
    {
        var neutral = (Brush)FindResource("SecondaryBrush");
        var active = (Brush)FindResource("AccentBrush");
        if (Kind == "Mouse")
        {
            DrawMouse(dc, neutral, new Rect(1, 1, 17, 24));
            return;
        }
        int columns = Kind == "Full" ? 6 : Kind == "Custom" ? 3 : 4;
        int rows = Kind == "Custom" ? 1 : 2;
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < columns; col++)
            dc.DrawRoundedRectangle(row == 0 && col == 0 && Kind == "Compact" ? active : neutral,
                null, new Rect(col * 10, row * 11 + 3, 8, 8), 2, 2);
        if (Kind is "Compact" or "Full")
            DrawMouse(dc, neutral, new Rect(columns * 10 + 3, 2, 14, 22));
    }
    private static void DrawMouse(DrawingContext dc, Brush brush, Rect bounds)
    {
        Geometry Shape(string path)
        {
            var geometry = Geometry.Parse(path).Clone();
            geometry.Transform = new MatrixTransform(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y);
            return geometry;
        }
        var pen = new Pen(brush, 1.25) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, Shape(MouseContours.Shell));
        dc.DrawGeometry(brush, null, Shape(MouseContours.Wheel));
        dc.DrawLine(pen, new(bounds.X + bounds.Width / 2, bounds.Y + 1),
            new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height * .14));
        dc.DrawLine(pen, new(bounds.X + bounds.Width * .1, bounds.Y + bounds.Height * .465),
            new(bounds.X + bounds.Width * .9, bounds.Y + bounds.Height * .465));
    }

}
