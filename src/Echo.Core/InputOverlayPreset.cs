using System.Globalization;
using System.Text;

namespace Echo.Core;

public sealed record InputOverlayPreset
{
    public bool Enabled { get; init; } = true;
    public bool ShowApm { get; init; } = true;
    public bool Collapsed { get; init; }
    public string Layout { get; init; } = "Compact";
    public string Style { get; init; } = "Dark";
    public double X { get; init; } = .035;
    public double Y { get; init; } = .72;
    public double Width { get; init; } = .34;
    public void Validate()
    {
        if (Layout is not ("Compact" or "Full" or "Mouse") || Style is not ("Dark" or "Light" or "Outline")) throw new ArgumentException("Choose an input overlay style and layout.");
        if (new[] { X, Y, Width }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Width < .15 || Width > .85) throw new ArgumentException("Check input overlay size and position.");
    }
}
public sealed record OverlayLayout(int Width, int Height, IReadOnlyList<InputKey> Keys);
public sealed record OverlayColors(string Panel, string Key, string Pressed, string Text, string Line, string PanelAlpha);
public static class OverlayAppearance
{
    private static readonly OverlayLayout Compact = new(376, 164, GameplayInputs.Keys.Where(k => k.Y < 156).ToArray());
    private static readonly OverlayLayout Mouse = new(156, 230, [new("MouseLeft","LMB",20,48,54,64),new("MouseRight","RMB",80,48,54,64),new("MouseMiddle","MMB",20,118,114,30),new("Mouse4","M4",20,154,54),new("Mouse5","M5",80,154,54),new("WheelUp","+",20,190,54),new("WheelDown","−",80,190,54)]);
    private static readonly OverlayLayout Full = MakeFull();
    public static OverlayLayout Layout(string name) => name == "Full" ? Full : name == "Mouse" ? Mouse : Compact;
    public static IEnumerable<string> AllControls => GameplayInputs.Keys.Select(k => k.Id).Concat(Full.Keys.Select(k => k.Id)).Distinct();
    public static OverlayColors Colors(string style) => style switch
    {
        "Light" => new("F2F5FA","FFFFFF","8CB7F0","192338","A5B1C3","18"),
        "Outline" => new("151922","151922","507FBF","F4F7FF","A9BCD7","FF"),
        _ => new("131A25","263246","527FC0","F3F6FF","667B9C","25")
    };
    private static OverlayLayout MakeFull()
    {
        var keys = new List<InputKey>();
        for (int i = 0; i < 12; i++) keys.Add(new("F"+(i+1),"F"+(i+1),12+i*44,42,38));
        void Row(string chars, int y, int x = 12) { for (int i=0;i<chars.Length;i++) keys.Add(new(chars[i].ToString(),chars[i].ToString(),x+i*44,y,38)); }
        Row("1234567890",82); keys.Add(new("Backspace","BACK",452,82,72));
        keys.Add(new("Tab","TAB",12,122,54)); Row("QWERTYUIOP",122,72);
        keys.Add(new("Caps","CAPS",12,162,66)); Row("ASDFGHJKL",162,84);
        keys.Add(new("Shift","SHIFT",12,202,86)); Row("ZXCVBNM",202,104);
        keys.AddRange([new("Ctrl","CTRL",12,242,64),new("Alt","ALT",82,242,56),new("Space","SPACE",144,242,256),new("Left","←",406,242,34),new("Down","↓",446,242,34),new("Right","→",486,242,34),new("Up","↑",486,202,34)]);
        keys.AddRange([new("MouseLeft","LMB",568,82,56,64),new("MouseRight","RMB",630,82,56,64),new("MouseMiddle","MMB",568,152,118),new("Mouse4","M4",568,188,56),new("Mouse5","M5",630,188,56),new("WheelUp","+",568,224,56),new("WheelDown","−",630,224,56)]);
        return new(704,284,keys);
    }
}

public static class InputOverlayExport
{
    private static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Stamp(double seconds) { var t=TimeSpan.FromSeconds(Math.Max(0,seconds)); return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds/10:00}"; }
    private static string Bgr(string rgb) => rgb[4..6]+rgb[2..4]+rgb[0..2];
    public static string Build(MediaClip clip, double start, double end, string? inputPath, InputOverlayPreset preset, int width, int height)
    {
        preset.Validate(); var layout=OverlayAppearance.Layout(preset.Layout); var colors=OverlayAppearance.Colors(preset.Style); var samples=GameplayInputs.Read(inputPath); var hasData=inputPath!=null&&File.Exists(inputPath);
        var h=preset.Collapsed?38:layout.Height; var scale=Math.Min(width*preset.Width/layout.Width,(double)height/h);
        var x=Math.Clamp(width*preset.X,0,width-layout.Width*scale); var y=Math.Clamp(height*preset.Y,0,Math.Max(0,height-h*scale)); var length=end-start;
        var result=new StringBuilder($"[Script Info]\nScriptType: v4.00+\nPlayResX: {width}\nPlayResY: {height}\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Arial,16,&H00{Bgr(colors.Text)},&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        void Event(int layer,double a,double b,string text) { if(b-a>.009)result.AppendLine($"Dialogue: {layer},{Stamp(a)},{Stamp(b)},Default,,0,0,0,,{text}"); }
        void Box(int layer,double a,double b,double bx,double by,double w,double bh,string color,string alpha="00") => Event(layer,a,b,$"{{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H{Bgr(color)}&\\1a&H{alpha}&\\p1}}m {N(bx)} {N(by)} l {N(bx+w)} {N(by)} {N(bx+w)} {N(by+bh)} {N(bx)} {N(by+bh)}{{\\p0}}");
        void Label(double a,double b,string text,double lx,double ly,double fs=13) => Event(4,a,b,$"{{\\an7\\pos({N(lx)},{N(ly)})\\fs{N(fs*scale)}\\bord0\\shad0}}{text}");
        Box(0,0,length,x,y,layout.Width*scale,h*scale,colors.Panel,colors.PanelAlpha);
        if(!preset.Collapsed) foreach(var key in layout.Keys)
        {
            Box(1,0,length,x+key.X*scale,y+key.Y*scale,key.Width*scale,key.Height*scale,colors.Line);
            Box(2,0,length,x+(key.X+1)*scale,y+(key.Y+1)*scale,(key.Width-2)*scale,(key.Height-2)*scale,colors.Key,preset.Style=="Outline"?"A0":"00");
            Label(0,length,key.Label,x+(key.X+6)*scale,y+(key.Y+key.Height/2-7)*scale,key.Label.Length>2?10:14);
        }
        var boundaries=samples.Select(s=>PlaybackNavigation.FileTime(clip,s.Time)).Where(t=>t>start&&t<end).Select(t=>t!.Value).Concat(clip.Segments.Select(s=>s.FileStart).Where(t=>t>start&&t<end)).Append(start).Append(end).Distinct().Order().ToArray();
        var held=new HashSet<string>();int cursor=0;
        if(!preset.Collapsed)for(int i=0;i+1<boundaries.Length;i++)
        {
            var at=GameplayInputs.RecordingTime(clip,boundaries[i]+.0001);
            while(cursor<samples.Count&&samples[cursor].Time<=at){var sample=samples[cursor++];if(sample.Down)held.Add(sample.Control);else held.Remove(sample.Control);}
            foreach(var key in layout.Keys.Where(k=>held.Contains(k.Id)))Box(3,boundaries[i]-start,boundaries[i+1]-start,x+(key.X+1)*scale,y+(key.Y+1)*scale,(key.Width-2)*scale,(key.Height-2)*scale,colors.Pressed);
        }
        var presses=samples.Where(s=>s.Down).Select(s=>s.Time).ToArray();int low=0,high=0;
        for(double t=0;t<length;t+=1){var at=GameplayInputs.RecordingTime(clip,start+t);while(high<presses.Length&&presses[high]<=at)high++;while(low<high&&presses[low]<=at-60)low++;Label(t,Math.Min(length,t+1),preset.ShowApm?hasData?$"{high-low} APM":"— APM":"INPUTS",x+12*scale,y+10*scale,15);}
        return result.ToString();
    }
}
