using System.IO;
using System.Text;
using System.Windows.Media;
using Echo.Core;
namespace Echo.App;
internal static class InputOverlayExport
{
    private static string Stamp(double seconds) { var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds/10:00}"; }
    private static string Bgr(string rgb)=>rgb[4..6]+rgb[2..4]+rgb[0..2];
    internal static string Build(MediaClip clip,double start,double end,string? inputPath,InputOverlayPreset preset,int width,int height)
        => Build(clip, start, end, new GameplayInputPlayback(GameplayInputs.Read(inputPath)), File.Exists(inputPath), preset, width, height);

    internal static string Build(MediaClip clip, double start, double end, GameplayInputPlayback playback, bool hasData,
        InputOverlayPreset preset, int width, int height)
    {
        preset.Validate();var art=OverlayArtwork.Get(preset);var colors=OverlayAppearance.Colors(preset.Style);
        var h=preset.Collapsed?38:art.Height;var scale=Math.Min(width*preset.Width/art.Width,(double)height/h);var x=Math.Clamp(width*preset.X,0,width-art.Width*scale);var y=Math.Clamp(height*preset.Y,0,Math.Max(0,height-h*scale));var length=end-start;
        var text=new StringBuilder($"[Script Info]\nScriptType: v4.00+\nPlayResX: {width}\nPlayResY: {height}\nScaledBorderAndShadow: yes\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Arial,16,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        var paths = new Dictionary<Geometry, string>();
        void Draw(int layer,double a,double b,Geometry geometry,string color,string alpha="00")
        {
            if(b-a<.009||geometry.IsEmpty())return;
            if (!paths.TryGetValue(geometry, out var path))
                paths[geometry] = path = OverlayArtwork.AssPath(geometry, scale, x, y);
            text.AppendLine($"Dialogue: {layer},{Stamp(a)},{Stamp(b)},Default,,0,0,0,,{{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H{Bgr(color)}&\\1a&H{alpha}&\\p1}}{path}{{\\p0}}");
        }
        var idleAlpha=preset.Style=="Light"?"20":"60";
        if(!preset.Collapsed)foreach(var part in art.Parts.Where(p=>!p.Pulse))
        {
            if(preset.Style!="Outline")Draw(0,0,length,part.Fill,colors.Key,idleAlpha);
            Draw(2,0,length,part.Outline,colors.Line);if(part.Label!=null)Draw(3,0,length,part.Label,colors.Text);
        }
        if (!preset.Collapsed)
        {
            var segments = clip.Segments.Count == 0 ? [new MediaSegment(clip.RecordingOrigin, 0, end)] : clip.Segments;
            foreach (var segment in segments)
            {
                var from = Math.Max(start, segment.FileStart);
                var to = Math.Min(end, segment.FileStart + segment.Duration);
                if (from >= to) continue;
                var offset = segment.FileStart - segment.RecordingStart;
                foreach (var part in art.Parts.Where(p => p.Control != null))
                    foreach (var pulse in Pulses(part.Control!, from - offset, to - offset))
                    {
                        var a = Math.Max(from, pulse.Start + offset) - start;
                        var b = Math.Min(to, pulse.End + offset) - start;
                        Draw(1, a, b, part.Fill, colors.Pressed);
                        if (part.Label != null) Draw(4, a, b, part.Label, colors.Key);
                    }
            }
        }
        if (preset.ShowApm || preset.Collapsed)
        {
            for (double t = 0; t < length; t++)
            {
                var at = GameplayInputs.RecordingTime(clip, start + t);
                var label = preset.ShowApm ? hasData ? $"{playback.Apm(at)} APM" : "\u2014 APM" : "INPUTS";
                Draw(5, t, Math.Min(length, t + 1), OverlayArtwork.Header(label), colors.Text);
            }
        }
        return text.ToString();

        IEnumerable<TimeRange> Pulses(string control, double from, double to)
        {
            var pulses = playback.Pulses(control, from, to);
            return (preset.Layout is "Compact" or "Full") && (control is "Shift" or "Ctrl" or "Alt")
                ? pulses.Concat(playback.Pulses("Right" + control, from, to)) : pulses;
        }
    }
}
