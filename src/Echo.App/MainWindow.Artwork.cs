namespace Echo.App;

public partial class MainWindow
{
    private readonly ChampionArtwork _championArtwork;
    private bool _artworkPending;

    private void RequestChampionArtwork(ClipItem? opened = null)
    {
        if (CaptureActive())
        {
            _artworkPending = true;
            return;
        }
        _artworkPending = false;
        if ((opened ?? _selected) is { } selected) _ = ApplyArtworkAsync(selected, splash: false);
        foreach (var item in ClipList.Items.OfType<ClipItem>()) _ = ApplyArtworkAsync(item, splash: false);
        if (FeaturedPanel.DataContext is ClipItem featured)
        {
            _ = ApplyArtworkAsync(featured, splash: false);
            _ = ApplyArtworkAsync(featured, splash: true);
            foreach (var recent in RecentItems.Items.OfType<ClipItem>()) _ = ApplyArtworkAsync(recent, splash: false);
        }
    }

    private async Task ApplyArtworkAsync(ClipItem item, bool splash)
    {
        if (splash ? item.SplashRequested : item.IconRequested) return;
        if (splash) item.SplashRequested = true;
        else item.IconRequested = true;
        if (ChampionArtwork.Resolve(item.Session) is not { } id) return;

        var image = await _championArtwork.GetAsync(id, splash, () => !_closing && !CaptureActive());
        if (_closing) return;
        if (image != null) item.SetArtwork(image, splash);
        else if (CaptureActive())
        {
            if (splash) item.SplashRequested = false;
            else item.IconRequested = false;
            _artworkPending = true;
        }
    }
}
