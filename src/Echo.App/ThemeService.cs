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
            ("BackgroundBrush", "#0B0D0E", "#F0EFF5"),
            ("SidebarBrush", "#08090A", "#E7E5F0"),
            ("PanelBrush", "#15181A", "#FFFFFF"),
            ("RaisedBrush", "#1E2326", "#EAE7F1"),
            ("LineBrush", "#2A3034", "#CFC9DF"),
            ("TextBrush", "#E9EDEF", "#1E1B2E"),
            ("SecondaryBrush", "#A4ADB5", "#534C69"),
            ("AccentBrush", "#C5F65B", "#395B0B"),
            ("AccentTextBrush", "#172305", "#FFFFFF"),
            ("AccentSurfaceBrush", "#232D17", "#DFEDCA"),
            ("KillBrush", "#C5F65B", "#365B08"),
            ("AssistBrush", "#83DFDF", "#006A70"),
            ("DeathBrush", "#FF8FA0", "#9E263F"),
            ("FavoriteBrush", "#E2C3FF", "#624099"),
            ("NavigationBrush", "#222D16", "#E0ECD1"),
            ("NavigationActiveBrush", "#C5F65B", "#395B0B"),
            ("NavigationTextBrush", "#172305", "#FFFFFF"),
            ("ActivityBrush", "#C5F65B", "#395B0B"),
            ("StripTrackBrush", "#2F373C", "#D9D1E5")
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
