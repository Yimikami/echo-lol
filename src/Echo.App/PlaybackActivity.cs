using System.Windows;

namespace Echo.App;
public partial class MainWindow
{
    private void UpdateActivityVisibility()
    {
        bool visible=!_trimOpen||_fullscreen;
        PlaybackActivityPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        PlayerDock.RowDefinitions[1].Height=new(visible?136:22);
        Seek.Visibility=visible?Visibility.Collapsed:Visibility.Visible;
    }
    private void RefreshPlaybackActivity()
    {
        PlaybackActivity.Start=Timeline.SelectionStart;PlaybackActivity.End=Timeline.SelectionEnd;PlaybackActivity.Moments=Timeline.Moments;PlaybackActivity.Position=Timeline.Position;
        var values=PlaybackActivity.Activity.Where(p=>p.Time>=Timeline.SelectionStart&&p.Time<=Timeline.SelectionEnd).ToArray();
        ApmSummary.Text=values.Length==0?"APM —":$"Avg {values.Average(p=>p.Apm):0} APM   ·   Peak {values.Max(p=>p.Apm)} APM";
        PlaybackActivity.InvalidateVisual();
    }
}
