using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Echo.Core;
using Echo.Recorder;
namespace Echo.App;
internal static class OverlayVerification
{
    internal static async Task Run(HostPaths paths)
    {
        var checks=new List<string>();void Check(string name,bool value){if(!value)throw new Exception(name);checks.Add("PASS "+name);}
        File.WriteAllText(Path.Combine(paths.Root,"font-identity.txt"),OverlayArtwork.FontIdentity);
        Check("overlay lettering resolves the bundled Bebas Neue face",OverlayArtwork.FontIdentity.StartsWith("Bebas Neue|",StringComparison.Ordinal));
        var preview = new Canvas{Width=1184,Height=440,Background=new SolidColorBrush(Color.FromRgb(37,40,45))};
        foreach(var item in new[]{("Full",20d,24d,704d),("Compact",744d,78d,420d)})
        {
            var control=new Controls.InputOverlay{Preset=new(){Layout=item.Item1,Style="Outline",ShowApm=false},Width=item.Item4};control.Height=control.Width/control.Aspect;
            Canvas.SetLeft(control,item.Item2);Canvas.SetTop(control,item.Item3);preview.Children.Add(control);
        }
        await Render(preview,Path.Combine(paths.Root,"reference-style.png"),new(1184,440));
        Check("wireframe artwork uses frozen vector contours",OverlayArtwork.Get("Full").Parts.All(p=>p.Fill.IsFrozen&&p.Outline.IsFrozen));
        Check("mouse regions have separate button contours",OverlayArtwork.Get("Compact").Parts.Count(p=>p.Control?.StartsWith("Mouse")==true)==5);
        var folder=Path.Combine(paths.Root,"recordings","fixture");Directory.CreateDirectory(folder);var video=Path.Combine(folder,"source.mp4");var log=Path.Combine(folder,"inputs.jsonl");
        if(!File.Exists(video))MediaTools.Run("ffmpeg",["-hide_banner","-v","error","-n","-f","lavfi","-i","testsrc2=size=640x360:rate=30:duration=4","-f","lavfi","-i","sine=frequency=440:duration=4","-c:v","h264_nvenc","-preset","p3","-c:a","aac",video]);
        InputSample[] samples=[new(.2,"Q",true),new(.8,"Q",false),new(1,"MouseRight",true),new(1.5,"MouseRight",false)];File.WriteAllLines(log,samples.Select(s=>JsonSerializer.Serialize(s)));
        var poster=Path.Combine(folder,"clip.jpg");MediaTools.Thumbnail(video,poster,.4);
        var session=new Session{Id="fixture",EndedAt=DateTimeOffset.UtcNow,Duration=4,InputLogPath=log,Encoder="obs_nvenc_h264_tex",RecordingOptions=new(){Width=640,Height=360}};
        session.Moments.AddRange([new(1,MomentKind.Kill,.6,.6),new(2,MomentKind.Assist,1.2,1.2),new(3,MomentKind.Death,2.1,2.1)]);
        var clip=new MediaClip("clip",video,.4,2.4,"Kill",false){ThumbnailPath=poster};session.Clips.Add(clip);JsonStore.Write(Path.Combine(folder,"session.json"),session);
        JsonStore.Write(paths.Settings,new EchoSettings{OutputDirectory=Path.Combine(paths.Root,"recordings"),SetupCompleted=true});var original=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(video));
        foreach(var item in new[]{("Compact","Dark"),("Full","Light"),("Mouse","Outline"),("Custom","Outline")})
        {
            var preset=new InputOverlayPreset{Layout=item.Item1,Style=item.Item2,Width=item.Item1=="Mouse"?.18:.5};var output=Path.Combine(paths.Root,item.Item1+".mp4");
            await Task.Run(()=>ClipExport.Save(clip,.4,2.4,log,session.Encoder,preset,output,Path.Combine(paths.Root,"cache"),()=>false));
            Check(item.Item1+" overlay export has expected duration",Math.Abs(MediaTools.Duration(output)-2)<.1);MediaTools.Run("ffmpeg",["-v","error","-i",output,"-f","null","-"]);Check(item.Item1+" overlay export decodes",true);MediaTools.Thumbnail(output,Path.Combine(paths.Root,item.Item1+".jpg"),.1,960);
        }
        Check("all exports preserve the source",original.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(video))));
        var custom=new InputOverlayPreset{Layout="Custom"};var customArt=OverlayArtwork.Get(custom);
        Check("custom artwork excludes hidden keys and includes six",customArt.Parts.Any(p=>p.Control=="6")&&!customArt.Parts.Any(p=>p.Control=="A"));
        Check("mouse can be hidden from custom artwork",OverlayArtwork.Get(custom with{ShowMouse=false}).Parts.All(p=>p.Control?.StartsWith("Mouse")!=true));
        var window=new MainWindow(paths,false);try{await window.VerifyPlaybackOverlay(paths.Root,Check);}finally{window.Close();}
        var skipPaths=new HostPaths(Path.Combine(paths.Root,"skip-profile"));var skip=new OnboardingWindow(skipPaths);skip.VerifySkip(Check);skip.Close();
        var setupPaths=new HostPaths(Path.Combine(paths.Root,"setup-profile"));var setup=new OnboardingWindow(setupPaths);await setup.VerifySetup(paths.Root,Check);setup.Close();
        ThemeService.Apply("Dark");File.WriteAllLines(Path.Combine(paths.Root,"overlay-checks.txt"),checks);
    }
    internal static async Task Render(FrameworkElement root,string file,Size size)
    {
        root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(file);png.Save(output);
    }
}
public partial class MainWindow
{
    internal async Task VerifyPlaybackOverlay(string output,Action<string,bool> check)
    {
        RefreshCatalog(true);ClipList.SelectedIndex=0;PrepareVerificationPreview();await OverlayVerification.Render((FrameworkElement)Content,Path.Combine(output,"player.png"),new(1480,920));UpdatePlaybackInputs(.4);
        check("overlay appears in the normal player without a separate workspace",PlaybackInputs.Visibility==Visibility.Visible&&TrimWorkbench.Visibility==Visibility.Collapsed);
        check("input state maps to playback time",PlaybackInputs.Held.Contains("Q")&&PlaybackInputs.Apm==1);
        check("activity timeline is visible without opening trim",PlaybackActivityPanel.Visibility==Visibility.Visible&&!_trimOpen&&PlaybackActivity.ActualWidth>500);
        check("timeline includes kill assist and death markers",PlaybackActivity.Moments.Select(m=>m.Kind).Distinct().Count()==3&&PlaybackActivity.Activity.Count>0);
        PlaybackActivity.SeekFraction(.5);check("activity timeline can seek within the clip",Math.Abs(Timeline.Position-(PlaybackActivity.Start+PlaybackActivity.End)/2)<.01);
        check("APM summary comes from recorded input data",ApmSummary.Text.Contains("Peak 12 APM",StringComparison.Ordinal));
        var before=_inputPreset;_inputGrab=new(5,5);var bounds=VideoBounds();MoveInputLayer(new(bounds.X+bounds.Width*.65,bounds.Y+bounds.Height*.5));PersistInputPreset();
        check("dragging saves the overlay position",Math.Abs(_paths.LoadSettings().InputOverlay.X-before.X)>.1);
        InputResizeDelta(this,new DragDeltaEventArgs(30,0));InputResizeCompleted(this,new DragCompletedEventArgs(30,0,false));check("resizing saves overlay width",_paths.LoadSettings().InputOverlay.Width>before.Width);
        CollapsePlaybackInputs(this,new());check("overlay can collapse and expand",_inputPreset.Collapsed&&PlaybackInputs.Aspect>5);CollapsePlaybackInputs(this,new());
        _inputPreset=_inputPreset with{Layout="Full",Style="Outline"};PersistInputPreset();check("layout and style persist for every clip",_paths.LoadSettings().InputOverlay.Layout=="Full"&&_paths.LoadSettings().InputOverlay.Style=="Outline");
        await OverlayVerification.Render((FrameworkElement)Content,Path.Combine(output,"player-overlay.png"),new(1480,920));
        SettingsClick(this,new());ShowSettingsCategory("Inputs");await OverlayVerification.Render((FrameworkElement)Content,Path.Combine(output,"input-settings.png"),new(1480,1100));check("input settings have their own category",InputSettings.Visibility==Visibility.Visible&&RecordingSettings.Visibility==Visibility.Collapsed);
        var placement=InputPlacementPreview;
        check("game preview uses a 16:9 frame",placement.ActualWidth>0&&Math.Abs(placement.ActualWidth/placement.ActualHeight-16d/9)<.01);
        var expectedWidth=placement.Preset.Width-45/placement.ActualWidth;
        placement.MoveTo(new Point(placement.ActualWidth*.22,placement.ActualHeight*.32));placement.ResizeBy(-45);placement.Commit();
        var saved=_paths.LoadSettings().InputOverlay;
        check("pre-game placement saves position and size",Math.Abs(saved.X-.22)<.001&&Math.Abs(saved.Y-.32)<.001&&Math.Abs(saved.Width-expectedWidth)<.001);
        check("free placement is labelled custom",InputPositionBox.SelectedIndex==4);
        check("settings preview and normal playback share a preset",JsonSerializer.Serialize(PlaybackInputs.Preset)==JsonSerializer.Serialize(saved)&&JsonSerializer.Serialize(placement.Preset)==JsonSerializer.Serialize(saved));
        placement.MoveTo(new Point(placement.ActualWidth*2,placement.ActualHeight*2));placement.Commit();
        check("pre-game drag stays inside the video",saved.X<placement.Preset.X&&placement.Preset.X<1&&placement.Preset.Y<1);
        _inputPreset=saved;PersistInputPreset();
        var selector=new InputKeysWindow(new(){Layout="Custom"},preset=>{_inputPreset=preset;PersistInputPreset();});
        selector.SelectKey("A",true);selector.SelectKey("Q",false);selector.SelectMouse(false);
        check("key selection saves immediately for playback and export",_paths.LoadSettings().InputOverlay.Keys.Contains("A")&&!_paths.LoadSettings().InputOverlay.Keys.Contains("Q")&&!_paths.LoadSettings().InputOverlay.ShowMouse&&PlaybackInputs.Preset.Layout=="Custom");
        await OverlayVerification.Render((FrameworkElement)selector.Content,Path.Combine(output,"key-selection.png"),new(820,700));selector.Close();
        _inputPreset=new InputOverlayPreset{Layout="Custom",X=saved.X,Y=saved.Y,Width=saved.Width};PersistInputPreset();
        await LoadPlacementBackground();check("preview frame is cached locally from a recording",InputPlacementPreview.BackgroundPath==Path.Combine(_paths.Root,"overlay-preview.png")&&File.Exists(InputPlacementPreview.BackgroundPath));
        await OverlayVerification.Render((FrameworkElement)Content,Path.Combine(output,"input-settings.png"),new(1480,1100));
        await OverlayVerification.Render(placement,Path.Combine(output,"placement-preview.png"),new(placement.ActualWidth,placement.ActualHeight));
    }
}
public partial class OnboardingWindow
{
    internal void VerifySkip(Action<string,bool> check){ThemeChoice.SelectedIndex=1;InputsChoice.IsChecked=true;SkipClick(this,new());var settings=_paths.LoadSettings();check("skip keeps defaults and completes setup",settings.SetupCompleted&&settings.Theme=="Dark"&&!settings.RecordInputs&&settings.GameAudio&&!settings.DesktopAudio);}
    internal async Task VerifySetup(string output,Action<string,bool> check)
    {
        for(_step=0;_step<5;_step++){ShowStep();await OverlayVerification.Render((FrameworkElement)Content,Path.Combine(output,$"onboarding-{_step+1}.png"),new(960,760));check("onboarding step "+(_step+1)+" is reachable",Progress.Value==_step+1&&SetupSteps.Children.Count==5);}
        _step=2;ShowStep();BeforeChoice.Text="invalid";NextClick(this,new());check("onboarding validates timing before leaving highlights",_step==2&&SetupError.Text.Length>0);
        ThemeChoice.SelectedIndex=1;QualityChoice.SelectedIndex=0;ModeChoice.SelectedIndex=1;SourceChoice.SelectedIndex=1;BeforeChoice.Text="12";AfterChoice.Text="6";KillsChoice.IsChecked=true;DeathsChoice.IsChecked=false;DesktopChoice.IsChecked=true;MicChoice.IsChecked=true;InputsChoice.IsChecked=true;ShortcutChoice.Text="Ctrl+Alt+F9";FolderChoice.Text=Path.Combine(_paths.Root,"media");
        _step=4;NextClick(this,new());var settings=_paths.LoadSettings();check("setup persists recording quality and mode",settings.Height==720&&settings.Fps==30&&settings.Mode==RecordingMode.FullMatch);check("setup persists event audio shortcut and storage choices",!settings.CaptureDeaths&&settings.DesktopAudio&&settings.Microphone&&settings.ManualShortcut=="Ctrl+Alt+F9"&&settings.OutputDirectory==FolderChoice.Text&&settings.RecordInputs);check("light theme applies",settings.Theme=="Light"&&((SolidColorBrush)Application.Current.Resources["BackgroundBrush"]).Color.R>200);
    }
}
