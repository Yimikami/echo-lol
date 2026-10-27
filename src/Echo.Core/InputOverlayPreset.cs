namespace Echo.Core;

public sealed record InputOverlayPreset
{
    public bool Enabled { get; init; } = true;
    public bool ShowApm { get; init; } = true;
    public bool Collapsed { get; init; }
    public string Layout { get; init; } = "Compact";
    public string Style { get; init; } = "Outline";
    public string[] Keys { get; init; } = ["1","2","3","4","5","6","Tab","Q","W","E","R","S","D","F","X","Ctrl","Space"];
    public bool ShowMouse { get; init; } = true;
    public double X { get; init; } = .035;
    public double Y { get; init; } = .72;
    public double Width { get; init; } = .34;
    public void Validate()
    {
        if (Layout is not ("Compact" or "Full" or "Mouse" or "Custom") || Style is not ("Dark" or "Light" or "Outline")) throw new ArgumentException("Choose an input overlay style and layout.");
        if(Keys==null||Keys.Length>OverlayAppearance.KeyboardKeys.Count||Keys.Any(k=>!OverlayAppearance.KeyboardKeys.Contains(k))||Keys.Distinct(StringComparer.Ordinal).Count()!=Keys.Length)throw new ArgumentException("Choose valid, distinct overlay keys.");
        if (new[] { X, Y, Width }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Width < .15 || Width > .85) throw new ArgumentException("Check input overlay size and position.");
    }
}
public sealed record InputKey(string Id,string Label,int X,int Y,int Width=32,int Height=30);
public sealed record OverlayMouse(double X,double Y,double Width,double Height);
public sealed record OverlayLayout(int Width,int Height,IReadOnlyList<InputKey> Keys,OverlayMouse? Mouse);
public sealed record OverlayColors(string Key,string Pressed,string Text,string Line);
public static class OverlayAppearance
{
    public static IReadOnlyList<string[]> KeyboardRows { get; } = [
        Enumerable.Range(1,12).Select(i=>"F"+i).ToArray(),
        ["Backtick","1","2","3","4","5","6","7","8","9","0","Backspace"],
        ["Tab","Q","W","E","R","T","Y","U","I","O","P"],
        ["Caps","A","S","D","F","G","H","J","K","L"],
        ["Shift","Z","X","C","V","B","N","M","Up"],
        ["Ctrl","Alt","Space","Left","Down","Right"]
    ];
    public static IReadOnlySet<string> KeyboardKeys { get; }=KeyboardRows.SelectMany(r=>r).ToHashSet(StringComparer.Ordinal);
    public static string KeyLabel(string key)=>key switch { "Backtick"=>"~","Backspace"=>"BACK","Space"=>"SPACE","Left"=>"LEFT","Right"=>"RIGHT","Up"=>"UP","Down"=>"DOWN",_=>key.ToUpperInvariant() };
    private static readonly OverlayLayout Compact=MakeCompact();
    private static readonly OverlayLayout Full=MakeFull();
    private static readonly OverlayLayout Mouse=MakeMouse();
    public static OverlayLayout Layout(string name)=>name=="Full"?Full:name=="Mouse"?Mouse:Compact;
    public static OverlayLayout Layout(InputOverlayPreset preset)=>preset.Layout=="Custom"?MakeCustom(preset):Layout(preset.Layout);
    public static OverlayColors Colors(string style)=>style=="Light"?new("FFFFFF","151515","151515","151515"):new("000000","FFFFFF","FFFFFF","FFFFFF");
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
    private static OverlayLayout MakeCustom(InputOverlayPreset preset)
    {
        var selected=preset.Keys.ToHashSet(StringComparer.Ordinal);var keys=new List<InputKey>();int row=0,maxWidth=0;
        foreach(var sourceRow in KeyboardRows)
        {
            var visible=sourceRow.Where(selected.Contains).ToArray();if(visible.Length==0)continue;
            int x=12;
            foreach(var id in visible)
            {
                int width=id=="Space"?180:id.Length>1&&!id.StartsWith('F')?94:74;
                keys.Add(new(id,KeyLabel(id),x,36+row*68,width,62));x+=width-4;
            }
            maxWidth=Math.Max(maxWidth,x+12);row++;
        }
        var height=Math.Max(38,row*68+38);OverlayMouse? mouse=null;
        if(preset.ShowMouse){mouse=new(Math.Max(12,maxWidth+16),42,174,244);AddMouse(keys,mouse);height=Math.Max(height,296);maxWidth=(int)mouse.X+(int)mouse.Width+12;}
        return new(Math.Max(180,maxWidth),height,keys,mouse);
    }
}
