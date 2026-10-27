namespace Echo.Core;

public sealed record InputOverlayPreset
{
    public bool Enabled { get; init; } = true;
    public bool ShowApm { get; init; } = true;
    public bool Collapsed { get; init; }
    public string Layout { get; init; } = "Compact";
    public string Style { get; init; } = "Outline";
    public double X { get; init; } = .035;
    public double Y { get; init; } = .72;
    public double Width { get; init; } = .34;
    public void Validate()
    {
        if (Layout is not ("Compact" or "Full" or "Mouse") || Style is not ("Dark" or "Light" or "Outline")) throw new ArgumentException("Choose an input overlay style and layout.");
        if (new[] { X, Y, Width }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Width < .15 || Width > .85) throw new ArgumentException("Check input overlay size and position.");
    }
}
public sealed record OverlayMouse(double X,double Y,double Width,double Height);
public sealed record OverlayLayout(int Width,int Height,IReadOnlyList<InputKey> Keys,OverlayMouse Mouse);
public sealed record OverlayColors(string Panel,string Key,string Pressed,string Text,string Line,string PanelAlpha);
public static class OverlayAppearance
{
    private static readonly OverlayLayout Compact=MakeCompact();
    private static readonly OverlayLayout Full=MakeFull();
    private static readonly OverlayLayout Mouse=MakeMouse();
    public static OverlayLayout Layout(string name)=>name=="Full"?Full:name=="Mouse"?Mouse:Compact;
    public static IEnumerable<string> AllControls=>Full.Keys.Select(k=>k.Id).Concat(Compact.Keys.Select(k=>k.Id)).Distinct();
    public static OverlayColors Colors(string style)=>style=="Light"?new("FFFFFF","FFFFFF","151515","151515","151515","FF"):new("000000","000000","FFFFFF","FFFFFF","FFFFFF","FF");
    private static void AddMouse(List<InputKey> keys,OverlayMouse m)
    {
        void Add(string id,double x,double y,double w,double h)=>keys.Add(new(id,"",(int)(m.X+x*m.Width),(int)(m.Y+y*m.Height),Math.Max(2,(int)(w*m.Width)),Math.Max(2,(int)(h*m.Height))));
        Add("MouseLeft",.02,0,.46,.43);Add("MouseRight",.52,0,.46,.43);Add("MouseMiddle",.43,.20,.14,.22);Add("Mouse4",.01,.49,.12,.14);Add("Mouse5",.06,.67,.12,.14);Add("WheelUp",.43,.20,.14,.10);Add("WheelDown",.43,.32,.14,.10);
    }
    private static OverlayLayout MakeFull()
    {
        var keys=new List<InputKey>();
        void Key(string id,string label,int col,int row,int width=74)=>keys.Add(new(id,label,12+70*col,38+68*row,width,62));
        Key("Backtick","~",0,0);for(int i=1;i<=5;i++)Key(i.ToString(),i.ToString(),i,0);
        Key("Tab","TAB",0,1);for(int i=0;i<5;i++)Key("QWERT"[i].ToString(),"QWERT"[i].ToString(),i+1,1);
        for(int i=0;i<5;i++)Key("ASDFG"[i].ToString(),"ASDFG"[i].ToString(),i+1,2);
        Key("Shift","SHIFT",0,3);for(int i=0;i<5;i++)Key("ZXCVB"[i].ToString(),"ZXCVB"[i].ToString(),i+1,3);
        Key("Ctrl","CTRL",0,4);Key("Alt","ALT",2,4);Key("Space","",3,4,214);
        var mouse=new OverlayMouse(474,58,210,296);AddMouse(keys,mouse);return new(704,388,keys,mouse);
    }
    private static OverlayLayout MakeCompact()
    {
        var keys=new List<InputKey>();for(int i=0;i<4;i++){keys.Add(new("QWER"[i].ToString(),"QWER"[i].ToString(),12+i*55,36,60,56));keys.Add(new("ASDF"[i].ToString(),"ASDF"[i].ToString(),i*55,98,60,56));}
        keys.Add(new("Shift","",0,160,68,56));keys.Add(new("Space","",72,160,153,56));var mouse=new OverlayMouse(244,126,164,170);AddMouse(keys,mouse);return new(420,306,keys,mouse);
    }
    private static OverlayLayout MakeMouse(){var keys=new List<InputKey>();var mouse=new OverlayMouse(10,36,156,226);AddMouse(keys,mouse);return new(180,278,keys,mouse);}
}
