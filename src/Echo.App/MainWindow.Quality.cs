using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private QualityRecommendation? _qualitySuggestion;

    private async Task LoadQualitySuggestion()
    {
        if (_qualitySuggestion != null) return;
        var hardware = await CaptureHardwareProbe.GetAsync();
        if (_closing) return;
        _qualitySuggestion = QualityRecommendation.For(hardware);
        QualitySuggestion.Text = $"Suggested: {_qualitySuggestion.Label}";
        QualityHardware.Text = $"{hardware.GraphicsName} · {Math.Round(hardware.MemoryMb / 1024d)} GB RAM\n{_qualitySuggestion.Reason}";
        ApplyRecommendedQuality.IsEnabled = true;
    }

    private void ApplyQualityRecommendationClick(object sender, RoutedEventArgs e)
    {
        if (_qualitySuggestion is not { } quality) return;
        _updating = true;
        try
        {
            ResolutionBox.SelectedIndex = quality.Height == 720 ? 0 : 1;
            FpsBox.SelectedIndex = quality.Fps == 30 ? 0 : 1;
        }
        finally { _updating = false; }
        AutoSave(false);
    }
}
