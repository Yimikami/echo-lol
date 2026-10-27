using System.Globalization;
using System.IO;
using System.Text;
using Echo.Core;
namespace Echo.App;
internal static class InputOverlayExport
{
    private static string Stamp(double seconds) { var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds/10:00}"; }
    private static string Bgr(string rgb)=>rgb[4..6]+rgb[2..4]+rgb[0..2];
    internal static string Build(MediaClip clip,double start,double end,string? inputPath,InputOverlayPreset preset,int width,int height)
    {
        preset.Validate();var art=OverlayArtwork.Get(preset);var colors=OverlayAppearance.Colors(preset.Style);var samples=GameplayInputs.Read(inputPath);var hasData=inputPath!=null&&File.Exists(inputPath);
        var h=preset.Collapsed?38:art.Height;var scale=Math.Min(width*preset.Width/art.Width,(double)height/h);var x=Math.Clamp(width*preset.X,0,width-art.Width*scale);var y=Math.Clamp(height*preset.Y,0,Math.Max(0,height-h*scale));var length=end-start;
        var text=new StringBuilder($"[Script Info]\nScriptType: v4.00+\nPlayResX: {width}\nPlayResY: {height}\nScaledBorderAndShadow: yes\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Arial,16,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        void Draw(int layer,double a,double b,System.Windows.Media.Geometry geometry,string color,string alpha="00")
        {
            if(b-a<.009||geometry.IsEmpty())return;
            text.AppendLine($"Dialogue: {layer},{Stamp(a)},{Stamp(b)},Default,,0,0,0,,{{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H{Bgr(color)}&\\1a&H{alpha}&\\p1}}{OverlayArtwork.AssPath(geometry,scale,x,y)}{{\\p0}}");
        }
        var idleAlpha=preset.Style=="Light"?"20":"60";
        if(!preset.Collapsed)foreach(var part in art.Parts.Where(p=>!p.Pulse))
        {
            if(preset.Style!="Outline")Draw(0,0,length,part.Fill,colors.Key,idleAlpha);
            Draw(2,0,length,part.Outline,colors.Line);if(part.Label!=null)Draw(3,0,length,part.Label,colors.Text);
        }
        var boundaries=samples.Select(s=>PlaybackNavigation.FileTime(clip,s.Time)).Where(t=>t>start&&t<end).Select(t=>t!.Value).Concat(clip.Segments.Select(s=>s.FileStart).Where(t=>t>start&&t<end)).Append(start).Append(end).Distinct().Order().ToArray();
        var held=new HashSet<string>();int cursor=0;
        if(!preset.Collapsed)for(int i=0;i+1<boundaries.Length;i++)
        {
            var at=GameplayInputs.RecordingTime(clip,boundaries[i]+.0001);while(cursor<samples.Count&&samples[cursor].Time<=at){var s=samples[cursor++];if(s.Down)held.Add(s.Control);else held.Remove(s.Control);}
            foreach(var part in art.Parts.Where(p=>p.Control!=null&&held.Contains(p.Control)))
            {
                Draw(1,boundaries[i]-start,boundaries[i+1]-start,part.Fill,colors.Pressed);
                if(part.Label!=null)Draw(4,boundaries[i]-start,boundaries[i+1]-start,part.Label,colors.Key);
            }
        }
        if(preset.ShowApm||preset.Collapsed)
        {
            var presses=samples.Where(s=>s.Down).Select(s=>s.Time).ToArray();int low=0,high=0;
            for(double t=0;t<length;t++){var at=GameplayInputs.RecordingTime(clip,start+t);while(high<presses.Length&&presses[high]<=at)high++;while(low<high&&presses[low]<=at-60)low++;Draw(5,t,Math.Min(length,t+1),OverlayArtwork.Header(preset.ShowApm?hasData?$"{high-low} APM":"— APM":"INPUTS"),colors.Text);}
        }
        return text.ToString();
    }
}
