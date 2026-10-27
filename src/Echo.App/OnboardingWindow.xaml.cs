using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Echo.Core;
using Microsoft.Win32;
namespace Echo.App;
public partial class OnboardingWindow : Window
{
    private readonly HostPaths _paths; private readonly EchoSettings _original; private int _step; private bool _ready,_saved;
    internal OnboardingWindow(HostPaths paths)
    {
        _paths=paths; _original=paths.LoadSettings(); InitializeComponent();
        ThemeChoice.SelectedIndex=_original.Theme=="Light"?1:_original.Theme=="System"?2:0;
        QualityChoice.SelectedIndex=(_original.Height==720?0:2)+(_original.Fps==60?1:0);ModeChoice.SelectedIndex=_original.Mode==RecordingMode.FullMatch?1:0; SourceChoice.SelectedIndex=_original.CaptureMethod==CaptureMethod.FullscreenDisplay?1:0;
        KillsChoice.IsChecked=_original.CaptureKills;AssistsChoice.IsChecked=_original.CaptureAssists;DeathsChoice.IsChecked=_original.CaptureDeaths;
        BeforeChoice.Text=_original.BeforeSeconds.ToString();AfterChoice.Text=_original.AfterSeconds.ToString();ShortcutChoice.Text=_original.ManualShortcut;InputsChoice.IsChecked=_original.RecordInputs;
        GameChoice.IsChecked=_original.GameAudio;DesktopChoice.IsChecked=_original.DesktopAudio;MicChoice.IsChecked=_original.Microphone;FolderChoice.Text=_original.OutputDirectory;
        _ready=true;ShowStep(); Closing+=(_,_)=>{if(!_saved)SaveDefaults();};
    }
    private void ShowStep(){FrameworkElement[] panels=[AppearanceStep,CaptureStep,HighlightsStep,AudioStep,StorageStep];for(int i=0;i<panels.Length;i++)panels[i].Visibility=i==_step?Visibility.Visible:Visibility.Collapsed;string[] titles=["Make it yours","Choose your recording","Keep the moments you want","Choose your audio","Where should clips go?"];StepTitle.Text=titles[_step];StepNumber.Text=$"{_step+1} / 5";Progress.Value=_step+1;BackButton.Visibility=_step==0?Visibility.Collapsed:Visibility.Visible;NextButton.Content=_step==4?"Start using Echo":"Continue";SetupError.Text="";}
    private void ThemeChanged(object sender,SelectionChangedEventArgs e){if(_ready)ThemeService.Apply(ThemeChoice.SelectedIndex==1?"Light":ThemeChoice.SelectedIndex==2?"System":"Dark");}
    private void BackClick(object sender,RoutedEventArgs e){if(_step>0)_step--;ShowStep();}
    private void NextClick(object sender,RoutedEventArgs e){if(_step<4){_step++;ShowStep();return;}try{var settings=Collect();settings.Validate();JsonStore.Write(_paths.Settings,settings);_saved=true;ThemeService.Apply(settings.Theme);Close();}catch(Exception ex){SetupError.Text=ex.Message;}}
    private EchoSettings Collect()
    {
        var before=int.Parse(BeforeChoice.Text,CultureInfo.InvariantCulture);var after=int.Parse(AfterChoice.Text,CultureInfo.InvariantCulture);var buffer=Math.Max(40,before+after+5);var bitrate=QualityChoice.SelectedIndex<2?6000:12000;
        return _original with {SetupCompleted=true,Theme=ThemeChoice.SelectedIndex==1?"Light":ThemeChoice.SelectedIndex==2?"System":"Dark",Width=QualityChoice.SelectedIndex<2?1280:1920,Height=QualityChoice.SelectedIndex<2?720:1080,Fps=QualityChoice.SelectedIndex%2==0?30:60,BitrateKbps=bitrate,Mode=ModeChoice.SelectedIndex==1?RecordingMode.FullMatch:RecordingMode.Clips,CaptureMethod=SourceChoice.SelectedIndex==1?CaptureMethod.FullscreenDisplay:CaptureMethod.Window,CaptureKills=KillsChoice.IsChecked==true,CaptureAssists=AssistsChoice.IsChecked==true,CaptureDeaths=DeathsChoice.IsChecked==true,BeforeSeconds=before,AfterSeconds=after,BufferSeconds=buffer,BufferMegabytes=Math.Max(128,(int)Math.Ceiling(bitrate*1000d/8*buffer*1.3/1048576)),ManualShortcut=ShortcutGesture.Parse(ShortcutChoice.Text)?.Text??"",RecordInputs=InputsChoice.IsChecked==true,GameAudio=GameChoice.IsChecked==true,DesktopAudio=DesktopChoice.IsChecked==true,Microphone=MicChoice.IsChecked==true,OutputDirectory=FolderChoice.Text.Trim()};
    }
    private void SaveDefaults(){JsonStore.Write(_paths.Settings,_original with {SetupCompleted=true});ThemeService.Apply(_original.Theme);_saved=true;}
    private void SkipClick(object sender,RoutedEventArgs e){SaveDefaults();Close();}
    private void BrowseFolder(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="Choose recording folder"};if(dialog.ShowDialog(this)==true)FolderChoice.Text=dialog.FolderName;}
    private void ShortcutKeyDown(object sender,KeyEventArgs e){e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)return;if(key is Key.Back or Key.Delete){ShortcutChoice.Text="";return;}var parts=new List<string>();var mods=Keyboard.Modifiers;if(mods.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");if(mods.HasFlag(ModifierKeys.Alt))parts.Add("Alt");if(mods.HasFlag(ModifierKeys.Shift))parts.Add("Shift");var vk=KeyInterop.VirtualKeyFromKey(key);parts.Add(vk is >=48 and <=90?((char)vk).ToString():key.ToString());try{ShortcutChoice.Text=ShortcutGesture.Parse(string.Join('+',parts))?.Text??"";SetupError.Text="";}catch(ArgumentException ex){SetupError.Text=ex.Message;}}
}
