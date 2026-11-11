using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Echo.Core;

namespace Echo.App;

internal sealed record OverlayArtPart(string? Control, Geometry Fill, Geometry Outline, Geometry? Label, bool Pulse = false);
internal sealed record OverlayArt(int Width, int Height, IReadOnlyList<OverlayArtPart> Parts);

/// <summary>One frozen vector drawing is used by the player and ASS export, including glyph outlines.</summary>
internal static class OverlayArtwork
{
    private static readonly ConcurrentDictionary<string, OverlayArt> Cache = new();
    private static readonly ConcurrentDictionary<string, Geometry> HeaderCache = new();
    private static readonly GlyphTypeface KeyFont = new(new Uri("pack://application:,,,/Echo;component/Assets/Fonts/BebasNeue-Regular.ttf"));
    internal static string FontIdentity => string.Join(";",KeyFont.FamilyNames.Values)+"|"+KeyFont.FontUri;
    internal static OverlayArt Get(string layout) => Cache.GetOrAdd(layout, name=>Create(OverlayAppearance.Layout(name)));
    internal static OverlayArt Get(InputOverlayPreset preset)
    {
        if(preset.Layout!="Custom")return Get(preset.Layout);
        var key="Custom:"+preset.ShowMouse+":"+string.Join(',',preset.Keys.Order(StringComparer.Ordinal));
        if(Cache.Count>64)Cache.Clear();
        return Cache.GetOrAdd(key,_=>Create(OverlayAppearance.Layout(preset)));
    }
    internal static Geometry Header(string value) { if(HeaderCache.Count>128)HeaderCache.Clear();return HeaderCache.GetOrAdd(value, text => Label(text, new Rect(10, 3, 155, 26), .08, false)); }
    private static Geometry Frozen(Geometry geometry)
    {
        var flattened = geometry.GetFlattenedPathGeometry(.06, ToleranceType.Absolute);
        flattened.Freeze(); return flattened;
    }
    private static Geometry Label(string text, Rect rect, double slant = .19, bool centered = true)
    {
        if (string.IsNullOrEmpty(text)) return Geometry.Empty;
        var indices=text.Select(c=>KeyFont.CharacterToGlyphMap.TryGetValue(c,out var index)?index:KeyFont.CharacterToGlyphMap['-']).ToArray();
        var advances=indices.Select(index=>KeyFont.AdvanceWidths[index]*64).ToArray();
        var run=new GlyphRun(KeyFont,0,false,64,1f,indices,new Point(),advances,null,null,null,null,null,null);
        var glyph = run.BuildGeometry(); var bounds = glyph.Bounds;
        if (bounds.IsEmpty || bounds.Height <= 0) return Geometry.Empty;
        var scale = Math.Min(rect.Height / Math.Max(45,bounds.Height), rect.Width / (bounds.Width + slant * bounds.Height));
        glyph.Transform = new MatrixTransform(new Matrix(scale, 0, -slant * scale, scale, 0, 0));
        var transformed = glyph.Bounds;
        var matrix = glyph.Transform.Value;
        matrix.Translate(rect.X - transformed.X + (centered ? (rect.Width - transformed.Width) / 2 : 0), rect.Y - transformed.Y + (rect.Height - transformed.Height) / 2);
        glyph.Transform = new MatrixTransform(matrix); return Frozen(glyph);
    }
    private static OverlayArt Create(OverlayLayout layout)
    {
        var parts = new List<OverlayArtPart>();
        foreach (var key in layout.Keys.Where(k => !k.Id.StartsWith("Mouse", StringComparison.Ordinal) && !k.Id.StartsWith("Wheel", StringComparison.Ordinal)))
        {
            var shear = key.Height * .22;
            Geometry shape = new RectangleGeometry(new Rect(0, 0, key.Width - shear, key.Height), 4, 4);
            shape.Transform = new MatrixTransform(new Matrix(1, 0, -.22, 1, key.X + shear, key.Y));
            shape = Frozen(shape);
            var outline = Frozen(shape.GetWidenedPathGeometry(new Pen(Brushes.Black, 2.1)));
            var label = Label(key.Label, new Rect(key.X + 10, key.Y + 9, Math.Max(1, key.Width - 20), key.Height - 18));
            parts.Add(new(key.Id, shape, outline, label));
        }
        if(layout.Mouse is not {} mouse)return new(layout.Width,layout.Height,parts);
        Geometry MousePath(string path, bool mirror = false)
        {
            var geometry = Geometry.Parse(path).Clone();
            var width = Math.Min(mouse.Width, mouse.Height * .72);
            var left = mouse.X + (mouse.Width - width) / 2;
            geometry.Transform = new MatrixTransform(new Matrix(mirror ? -width : width, 0, 0, mouse.Height, left + (mirror ? width : 0), mouse.Y));
            return Frozen(geometry);
        }
        void MousePart(string? control, Geometry shape, bool pulse = false)
            => parts.Add(new(control, shape, pulse ? Geometry.Empty : Frozen(shape.GetWidenedPathGeometry(new Pen(Brushes.Black, 2.5))), null, pulse));
        MousePart("MouseLeft", MousePath(MouseContours.Left));
        MousePart("MouseRight", MousePath(MouseContours.Left, true));
        MousePart("MouseMiddle", MousePath(MouseContours.Wheel));
        MousePart(null, MousePath(MouseContours.Palm));
        MousePart("Mouse4", MousePath(MouseContours.SideFront));
        MousePart("Mouse5", MousePath(MouseContours.SideBack));
        MousePart("WheelUp", MousePath("M .46,.215 L .54,.215 L .54,.275 L .46,.275 Z"), true);
        MousePart("WheelDown", MousePath("M .46,.315 L .54,.315 L .54,.375 L .46,.375 Z"), true);
        return new(layout.Width, layout.Height, parts);
    }
    internal static string AssPath(Geometry geometry, double scale, double x, double y)
    {
        var flat = geometry.GetFlattenedPathGeometry(.06, ToleranceType.Absolute); var transform = flat.Transform?.Value ?? Matrix.Identity;
        var result = new StringBuilder();
        string Point(Point point) { point = transform.Transform(point); return (x + point.X * scale).ToString("0.###", CultureInfo.InvariantCulture) + " " + (y + point.Y * scale).ToString("0.###", CultureInfo.InvariantCulture); }
        foreach (var figure in flat.Figures)
        {
            result.Append("m ").Append(Point(figure.StartPoint)).Append(' ');
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly) foreach (var point in poly.Points) result.Append("l ").Append(Point(point)).Append(' ');
                else if (segment is LineSegment line) result.Append("l ").Append(Point(line.Point)).Append(' ');
            }
            if (figure.IsClosed) result.Append("l ").Append(Point(figure.StartPoint)).Append(' ');
        }
        return result.ToString();
    }
}
