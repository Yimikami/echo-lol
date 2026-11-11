using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Echo.App.Controls;
using Echo.Core;
using Microsoft.Win32;
namespace Echo.App;
public partial class OnboardingWindow : Window
{
    private readonly HostPaths _paths; private readonly EchoSettings _original; private int _step; private bool _ready,_saved;
    private bool _qualityTouched, _applyingQuality;
    private QualityRecommendation? _qualityRecommendation;
    private readonly Task _recommendationTask;

    internal OnboardingWindow(HostPaths paths)
    {
        _paths=paths; _original=paths.LoadSettings(); InitializeComponent();
        var availableHeight=Math.Min(760,Math.Max(540,SystemParameters.WorkArea.Height-24));MinHeight=Math.Min(700,availableHeight);Height=availableHeight;
        FitSetupWidth(SystemParameters.WorkArea.Width - 24);
        ThemeChoice.SelectedIndex=_original.Theme=="Light"?1:_original.Theme=="System"?2:0;
        QualityChoice.SelectedIndex=(_original.Height==720?0:2)+(_original.Fps==60?1:0);ModeChoice.SelectedIndex=_original.Mode==RecordingMode.FullMatch?1:0; SourceChoice.SelectedIndex=_original.CaptureMethod==CaptureMethod.FullscreenDisplay?1:0;
        foreach (var box in SetupEvents.Children.OfType<CheckBox>())
            box.IsChecked = _original.Captures(Enum.Parse<MomentKind>((string)box.Tag));
        BeforeChoice.Text=_original.BeforeSeconds.ToString();AfterChoice.Text=_original.AfterSeconds.ToString();ShortcutChoice.Text=_original.ManualShortcut;InputsChoice.IsChecked=_original.RecordInputs;
        GameChoice.IsChecked=_original.GameAudio;DesktopChoice.IsChecked=_original.DesktopAudio;MicChoice.IsChecked=_original.Microphone;TracksChoice.IsChecked=_original.SeparateAudioTracks;FolderChoice.Text=_original.OutputDirectory;
        string[] labels = ["Appearance", "Recording", "Highlights", "Audio", "Storage"];
        for (var i = 0; i < labels.Length; i++)
        {
            var badge = new Border { Width = 27, Height = 27, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
            var label = new TextBlock { Text = labels[i], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(badge);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content, Tag = i, Style = (Style)FindResource("QuietButton"),
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 8, 6),
                MinHeight = 44, Margin = new Thickness(0, 0, 0, 5)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, labels[i]);
            button.Click += StepClick;
            SetupSteps.Children.Add(button);
        }
        _ready=true;ShowStep(); _recommendationTask=RecommendQuality(); Closing+=(_,_)=>{if(!_saved)SaveDefaults();};
    }
    internal void FitSetupWidth(double available)
    {
        var width = Math.Min(960, Math.Max(600, available));
        MinWidth = Math.Min(900, width);
        Width = width;
        SetupArtworkColumn.Width = new GridLength(width < 760 ? 0 : width < 900 ? 240 : 300);
        SetupArtwork.Visibility = width < 760 ? Visibility.Collapsed : Visibility.Visible;
        SetupContent.Margin = width < 900 ? new Thickness(24, 24, 24, 24) : new Thickness(35, 29, 46, 28);
        StepTitle.FontSize = width < 900 ? 48 : 59;
    }

    private void ShowStep()
    {
        FrameworkElement[] panels=[AppearanceStep,CaptureStep,HighlightsStep,AudioStep,StorageStep];for(int i=0;i<panels.Length;i++)panels[i].Visibility=i==_step?Visibility.Visible:Visibility.Collapsed;
        string[] titles=["Make it yours","What to record","Your highlights","Choose your sound","Ready to capture"];
        string[] descriptions=["Start with a theme you like.","Pick a quality preset, recording mode and source.","Choose your moments and how much action to keep.","Choose what people will hear in your recordings.","Choose a folder, then you’re ready for your next match."];
        StepTitle.Text=titles[_step];StepDescription.Text=descriptions[_step];StepNumber.Text=$"Step {_step + 1} of 5";Progress.Value=_step+1;
        BackButton.Visibility=_step==0?Visibility.Collapsed:Visibility.Visible;NextButton.Content=_step==4?"Start using Echo":"Continue";SetupError.Text="";SetupScroll.ScrollToTop();
        foreach (Button button in SetupSteps.Children)
        {
            var index = (int)button.Tag;
            var active = index == _step;
            var completed = index < _step;
            if (active)
                button.SetResourceReference(BackgroundProperty, "AccentSurfaceBrush");
            else
                button.Background = Brushes.Transparent;

            var content = (StackPanel)button.Content;
            var badge = (Border)content.Children[0];
            var label = (TextBlock)content.Children[1];
            label.SetResourceReference(TextBlock.ForegroundProperty, active ? "TextBrush" : "SecondaryBrush");
            badge.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : completed ? "AccentSurfaceBrush" : "LineBrush");
            badge.SetResourceReference(BackgroundProperty, active ? "AccentBrush" : completed ? "AccentSurfaceBrush" : "SidebarBrush");
            if (completed)
            {
                var check = new PhosphorIcon { Kind = "Check", Width = 14, Height = 14 };
                check.SetResourceReference(ForegroundProperty, "AccentBrush");
                badge.Child = check;
            }
            else
            {
                var number = new TextBlock
                {
                    Text = (index + 1).ToString(CultureInfo.InvariantCulture), FontSize = 13, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                number.SetResourceReference(TextBlock.ForegroundProperty, active ? "AccentTextBrush" : "SecondaryBrush");
                badge.Child = number;
            }
        }
        var sources=new List<string>();if(GameChoice.IsChecked==true&&DesktopChoice.IsChecked!=true)sources.Add("Game audio");if(DesktopChoice.IsChecked==true)sources.Add("Desktop audio");if(MicChoice.IsChecked==true)sources.Add("Microphone");
        SetupSummary.Text=$"{((ListBoxItem)QualityChoice.SelectedItem).Content} · {(ModeChoice.SelectedIndex==1?"Entire match":"Highlights")}\n{(sources.Count>0?string.Join(" + ",sources):"No audio")}";
    }
    private void ThemeChanged(object sender,SelectionChangedEventArgs e){if(_ready)ThemeService.Apply(ThemeChoice.SelectedIndex==1?"Light":ThemeChoice.SelectedIndex==2?"System":"Dark");}
    private void BackClick(object sender,RoutedEventArgs e){if(_step>0)_step--;ShowStep();}
    private void NextClick(object sender,RoutedEventArgs e){if(!ValidateStep())return;if(_step<4){_step++;ShowStep();return;}try{var settings=Collect();settings.Validate();JsonStore.Write(_paths.Settings,settings);_saved=true;ThemeService.Apply(settings.Theme);Close();}catch(Exception ex){SetupError.Text=ex.Message;}}
    private bool ValidateStep()
    {
        if(_step is 2 or 4&&(!int.TryParse(BeforeChoice.Text,out var before)||before is <1 or >60||!int.TryParse(AfterChoice.Text,out var after)||after is <0 or >30)){if(_step==4){_step=2;ShowStep();}SetupError.Text="Choose 1–60 seconds before and 0–30 seconds after a moment.";return false;}
        if(_step==4){try{if(string.IsNullOrWhiteSpace(FolderChoice.Text))throw new ArgumentException();_ = Path.GetFullPath(FolderChoice.Text);}catch(ArgumentException){SetupError.Text="Choose a valid recording folder.";return false;}}
        return true;
    }
    private void StepClick(object sender,RoutedEventArgs e){var next=(int)((Button)sender).Tag;if(next>_step&&!ValidateStep())return;_step=next;ShowStep();}
    private void SetupDrag(object sender,MouseButtonEventArgs e){if(e.OriginalSource is TextBlock or Image&&e.LeftButton==MouseButtonState.Pressed)DragMove();}
    private void SetupKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;SkipClick(this,e);}}
    private EchoSettings Collect()
    {
        var before=int.Parse(BeforeChoice.Text,CultureInfo.InvariantCulture);var after=int.Parse(AfterChoice.Text,CultureInfo.InvariantCulture);
        var bitrate=QualityRecommendation.Bitrate(QualityChoice.SelectedIndex<2?720:1080,QualityChoice.SelectedIndex%2==0?30:60);
        var choices = SetupEvents.Children.OfType<CheckBox>().Select(box =>
            new KeyValuePair<MomentKind, bool>(Enum.Parse<MomentKind>((string)box.Tag), box.IsChecked == true));
        return (_original with {SetupCompleted=true,Theme=ThemeChoice.SelectedIndex==1?"Light":ThemeChoice.SelectedIndex==2?"System":"Dark",Width=QualityChoice.SelectedIndex<2?1280:1920,Height=QualityChoice.SelectedIndex<2?720:1080,Fps=QualityChoice.SelectedIndex%2==0?30:60,BitrateKbps=bitrate,Mode=ModeChoice.SelectedIndex==1?RecordingMode.FullMatch:RecordingMode.Clips,CaptureMethod=SourceChoice.SelectedIndex==1?CaptureMethod.FullscreenDisplay:CaptureMethod.Window,CaptureKills=KillsChoice.IsChecked==true,CaptureAssists=AssistsChoice.IsChecked==true,CaptureDeaths=DeathsChoice.IsChecked==true,BeforeSeconds=before,AfterSeconds=after,ManualShortcut=ShortcutGesture.Parse(ShortcutChoice.Text)?.Text??"",RecordInputs=InputsChoice.IsChecked==true,GameAudio=GameChoice.IsChecked==true,DesktopAudio=DesktopChoice.IsChecked==true,Microphone=MicChoice.IsChecked==true,SeparateAudioTracks=TracksChoice.IsChecked==true,OutputDirectory=FolderChoice.Text.Trim()}).WithCaptureChoices(choices).FitReplayBuffer();
    }
    private async Task RecommendQuality()
    {
        var hardware = await CaptureHardwareProbe.GetAsync();
        if (_saved) return;
        _qualityRecommendation = QualityRecommendation.For(hardware);
        SetupQualitySuggestion.Text = $"Suggested: {_qualityRecommendation.Label}. {_qualityRecommendation.Reason}";
        SetupHardware.Text = $"{hardware.GraphicsName} · {Math.Round(hardware.MemoryMb / 1024d)} GB RAM";
        UseSuggestedQuality.IsEnabled = true;
        if (!_original.SetupCompleted && !_qualityTouched) ApplySuggestedQuality();
    }

    private void ApplySuggestedQuality()
    {
        if (_qualityRecommendation == null) return;
        _applyingQuality = true;
        try { QualityChoice.SelectedIndex = _qualityRecommendation.ChoiceIndex; }
        finally { _applyingQuality = false; }
    }

    private void UseSuggestedQualityClick(object sender, RoutedEventArgs e) => ApplySuggestedQuality();
    private void QualityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_applyingQuality) _qualityTouched = true;
    }

    private void SaveDefaults(){JsonStore.Write(_paths.Settings,_original with {SetupCompleted=true});ThemeService.Apply(_original.Theme);_saved=true;}
    private void SkipClick(object sender,RoutedEventArgs e){SaveDefaults();Close();}
    private void BrowseFolder(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="Choose recording folder"};if(dialog.ShowDialog(this)==true)FolderChoice.Text=dialog.FolderName;}
    private void ShortcutKeyDown(object sender,KeyEventArgs e){e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)return;if(key is Key.Back or Key.Delete){ShortcutChoice.Text="";return;}var parts=new List<string>();var mods=Keyboard.Modifiers;if(mods.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");if(mods.HasFlag(ModifierKeys.Alt))parts.Add("Alt");if(mods.HasFlag(ModifierKeys.Shift))parts.Add("Shift");var vk=KeyInterop.VirtualKeyFromKey(key);parts.Add(vk is >=48 and <=90?((char)vk).ToString():key.ToString());try{ShortcutChoice.Text=ShortcutGesture.Parse(string.Join('+',parts))?.Text??"";SetupError.Text="";}catch(ArgumentException ex){SetupError.Text=ex.Message;}}
}
