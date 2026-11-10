using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Echo.Core;

namespace Echo.App;

internal sealed class ChampionArtwork(string profile)
{
    private sealed record Catalogue(string Version, Dictionary<string, string[]> Names);

    private const string Cdn = "https://ddragon.leagueoflegends.com";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 3 * 1024 * 1024 };
    private static readonly Lazy<Catalogue> Champions = new(() =>
    {
        using var stream = typeof(ChampionArtwork).Assembly.GetManifestResourceStream("Echo.Champions.json")!;
        return JsonSerializer.Deserialize<Catalogue>(stream)!;
    });
    private static readonly Lazy<Dictionary<string, string>> Aliases = new(() =>
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, names) in Champions.Value.Names)
        {
            aliases[Normalize(id)] = id;
            foreach (var name in names) aliases[Normalize(name)] = id;
        }
        return aliases;
    });
    private readonly SemaphoreSlim _downloads = new(3);
    private readonly Dictionary<(string Id, bool Splash), Task<BitmapSource?>> _recent = new();
    private readonly string _folder = Path.Combine(profile, "champion-art");
    private Task<string?>? _latest;

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());

    internal static string? Resolve(Session session)
    {
        if (session.ChampionId is { Length: > 0 and <= 40 } raw && raw.All(char.IsAsciiLetterOrDigit))
            return Aliases.Value.GetValueOrDefault(raw) ?? raw;
        return Aliases.Value.GetValueOrDefault(Normalize(session.Champion));
    }

    internal Task<BitmapSource?> GetAsync(string id, bool splash, Func<bool> canDownload)
    {
        if (_recent.Remove((id, splash), out var recent) && (!recent.IsCompletedSuccessfully || recent.Result != null))
        {
            _recent[(id, splash)] = recent;
            return recent;
        }
        var limit = splash ? 3 : 40;
        if (_recent.Keys.Count(k => k.Splash == splash) >= limit)
            _recent.Remove(_recent.Keys.First(k => k.Splash == splash));
        return _recent[(id, splash)] = Task.Run(() => LoadAsync(id, splash, canDownload));
    }

    private async Task<BitmapSource?> LoadAsync(string id, bool splash, Func<bool> canDownload)
    {
        var file = Path.Combine(_folder, id + (splash ? "-splash.jpg" : "-icon.png"));
        if (File.Exists(file))
        {
            try { return Decode(await File.ReadAllBytesAsync(file), splash); }
            catch (Exception e) when (IsArtworkError(e)) { }
        }

        await _downloads.WaitAsync();
        try
        {
            if (!canDownload()) return null;
            string url = splash
                ? $"{Cdn}/cdn/img/champion/splash/{id}_0.jpg"
                : $"{Cdn}/cdn/{Champions.Value.Version}/img/champion/{id}.png";
            byte[] bytes;
            try { bytes = await Client.GetByteArrayAsync(url); }
            catch (HttpRequestException e) when (!splash && e.StatusCode == HttpStatusCode.NotFound)
            {
                Task<string?> versions;
                lock (_downloads) versions = _latest ??= LatestVersionAsync();
                var latest = await versions;
                if (latest == null || latest == Champions.Value.Version) return null;
                bytes = await Client.GetByteArrayAsync($"{Cdn}/cdn/{latest}/img/champion/{id}.png");
            }
            var image = Decode(bytes, splash);
            try
            {
                Directory.CreateDirectory(_folder);
                await File.WriteAllBytesAsync(file, bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return image;
        }
        catch (Exception e) when (IsArtworkError(e)) { return null; }
        finally { _downloads.Release(); }
    }

    private static async Task<string?> LatestVersionAsync()
    {
        var json = await Client.GetStringAsync($"{Cdn}/api/versions.json");
        var latest = JsonSerializer.Deserialize<string[]>(json)?.FirstOrDefault();
        return latest is { Length: < 20 } && latest.All(c => char.IsAsciiDigit(c) || c == '.') ? latest : null;
    }

    private static BitmapSource Decode(byte[] bytes, bool splash)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = splash ? 1120 : 80;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsArtworkError(Exception e) =>
        e is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException
            or NotSupportedException or FormatException or JsonException;
}
