using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace Echo.App;
internal static class ThemeService
{
    internal static void Apply(string choice)
    {
        var light = choice == "Light";
        if(choice=="System") light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0) is int value && value==1;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(light?Wpf.Ui.Appearance.ApplicationTheme.Light:Wpf.Ui.Appearance.ApplicationTheme.Dark);
        var colors = new Dictionary<string,string>
        {
            ["BackgroundBrush"]=light?"#F4F6FA":"#151922",["PanelBrush"]=light?"#FFFFFF":"#202733",["RaisedBrush"]=light?"#E8EDF5":"#2A3444",
            ["LineBrush"]=light?"#C3CEDD":"#465269",["TextBrush"]=light?"#17243A":"#F6F8FC",["SecondaryBrush"]=light?"#465773":"#BDC7D7",
            ["AccentBrush"]=light?"#356CC8":"#A9C9FF",["AccentTextBrush"]=light?"#FFFFFF":"#15203B",["SidebarBrush"]=light?"#E9EEF6":"#161C26"
        };
        foreach(var pair in colors) Application.Current.Resources[pair.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
}
