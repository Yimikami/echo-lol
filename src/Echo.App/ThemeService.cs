using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Echo.App;

internal static class ThemeService
{
    private static bool? _appliedLight;

    internal static void Apply(string choice)
    {
        bool light = choice == "Light" || choice == "System" &&
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1;
        if (_appliedLight == light) return;

        var theme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme);

        var colors = new (string Key, string Dark, string Light)[]
        {
            ("BackgroundBrush", "#0D0F10", "#F0EFF5"),
            ("SidebarBrush", "#090B0B", "#E7E5F0"),
            ("PanelBrush", "#1B1D1F", "#FFFFFF"),
            ("RaisedBrush", "#25282B", "#EAE7F1"),
            ("LineBrush", "#33373B", "#CFC9DF"),
            ("TextBrush", "#F6F8F9", "#1E1B2E"),
            ("SecondaryBrush", "#AEB5BD", "#534C69"),
            ("AccentBrush", "#C5F65B", "#395B0B"),
            ("AccentTextBrush", "#172305", "#FFFFFF"),
            ("AccentSurfaceBrush", "#2C361F", "#DFEDCA"),
            ("KillBrush", "#C5F65B", "#365B08"),
            ("AssistBrush", "#83DFDF", "#006A70"),
            ("DeathBrush", "#FFA2AF", "#9E263F"),
            ("FavoriteBrush", "#E2C3FF", "#624099"),
            ("NavigationBrush", "#231739", "#E4DAF6"),
            ("NavigationActiveBrush", "#6413E9", "#5424AE"),
            ("NavigationTextBrush", "#FFFFFF", "#FFFFFF"),
            ("ActivityBrush", "#EF58C7", "#A21979"),
            ("StripTrackBrush", "#3B4248", "#D9D1E5")
        };
        foreach (var (key, dark, bright) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? bright : dark));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        ApplicationAccentColorManager.Apply(((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color, theme, false);
        _appliedLight = light;
    }
}
