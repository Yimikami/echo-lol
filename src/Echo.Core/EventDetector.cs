using System.Text.Json;

namespace Echo.Core;

/// <summary>Consumes Riot events, not score deltas. Reset for each independent match epoch.</summary>
public sealed class EventDetector
{
    private readonly HashSet<int> _seen = [];
    private HashSet<string> _names = new(StringComparer.Ordinal);
    private string? _team;
    private double _lastGameTime = -1;
    private bool _initialized;

    public void Identify(JsonElement data)
    {
        if (!data.TryGetProperty("activePlayer", out var player)) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { "riotId", "riotIdGameName", "summonerName" })
            if (player.TryGetProperty(key, out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                names.Add(name.GetString()!);
        // Do not strip arbitrary # suffixes: Riot IDs can be ambiguous. Use API-provided aliases.
        if (data.TryGetProperty("allPlayers", out var players) && players.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in players.EnumerateArray())
            {
                if (!new[] { "riotId", "summonerName" }.Any(k => candidate.TryGetProperty(k, out var n) && names.Contains(n.GetString() ?? ""))) continue;
                if (candidate.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.String) _team = team.GetString();
                foreach (var key in new[] { "riotId", "riotIdGameName", "summonerName" })
                    if (candidate.TryGetProperty(key, out var name) && name.ValueKind == JsonValueKind.String) names.Add(name.GetString()!);
            }
        }
        _names = names;
    }

    public IReadOnlyList<Moment> Consume(JsonElement root, double gameTime, double recordingTime, bool baseline = false)
    {
        if (!double.IsFinite(gameTime) || !double.IsFinite(recordingTime) || gameTime < 0 || recordingTime < 0) return [];
        if (_lastGameTime >= 0 && gameTime + 5 < _lastGameTime)
            throw new InvalidOperationException("The game clock reset. Start a new recording session.");
        _lastGameTime = gameTime;
        if (root.TryGetProperty("events", out var nested)) root = nested;
        if (!root.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array) return [];
        List<Moment> result = [];
        foreach (var e in events.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("EventID", out var idValue) || idValue.ValueKind != JsonValueKind.Number || !idValue.TryGetInt32(out var id) || id < 0 || _seen.Contains(id)) continue;
            if (!e.TryGetProperty("EventName", out var name) || name.ValueKind != JsonValueKind.String)
            {
                _seen.Add(id);
                continue;
            }
            if (!e.TryGetProperty("EventTime", out var time) || time.ValueKind != JsonValueKind.Number || !time.TryGetDouble(out var t) || !double.IsFinite(t) || t < 0 || t > gameTime + 2) continue;
            _seen.Add(id);
            if (baseline || !_initialized || _names.Count == 0) continue;
            bool IsSelf(string key) => e.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String && _names.Contains(n.GetString()!);
            bool assisted = e.TryGetProperty("Assisters", out var assists) && assists.ValueKind == JsonValueKind.Array
                && assists.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && _names.Contains(a.GetString()!));
            bool killer = IsSelf("KillerName");
            int streak = e.TryGetProperty("KillStreak", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var number) ? number : 0;
            bool stole = killer && e.TryGetProperty("Stolen", out var stolen)
                && (stolen.ValueKind == JsonValueKind.True || stolen.ValueKind == JsonValueKind.String
                    && bool.TryParse(stolen.GetString(), out var flag) && flag);
            MomentKind? kind = name.GetString() switch
            {
                "ChampionKill" => IsSelf("VictimName") ? MomentKind.Death : killer ? MomentKind.Kill : assisted ? MomentKind.Assist : null,
                "Multikill" when killer && streak is >= 2 and <= 5 => MomentKind.MultiKill,
                "Ace" when IsSelf("Acer") || _team != null && e.TryGetProperty("AcingTeam", out var team)
                    && team.ValueKind == JsonValueKind.String && team.GetString() == _team => MomentKind.Ace,
                "BaronKill" when killer || assisted => MomentKind.Baron,
                "DragonKill" when killer || assisted => MomentKind.Dragon,
                "HeraldKill" when killer || assisted => MomentKind.Herald,
                "TurretKilled" when killer || assisted => MomentKind.Turret,
                "InhibKilled" when killer || assisted => MomentKind.Inhibitor,
                _ => null
            };
            var captureTime = recordingTime - (gameTime - t);
            if (kind.HasValue && captureTime >= 0)
                result.Add(new(id, kind.Value, t, captureTime) { Streak = streak, Stolen = stole });
        }
        _initialized = true;
        return result.OrderBy(m => m.RecordingTime).ToArray();
    }
}

public sealed class MomentPlanner(EchoSettings settings)
{
    public const double MergeGapSeconds = 3;
    private readonly List<ClipRange> _ranges = [];
    public IReadOnlyList<ClipRange> Ranges => _ranges;
    public void Add(Moment moment)
    {
        if (!settings.Captures(moment.Kind) || !double.IsFinite(moment.RecordingTime) || moment.RecordingTime < 0) return;
        if (_ranges.Any(r => r.Moments.Any(m => m.EventId == moment.EventId && m.Kind == moment.Kind))) return;
        var rule = settings.Rule(moment.Kind);
        _ranges.Add(new(Math.Max(0, moment.RecordingTime - rule.Before), moment.RecordingTime + rule.After, [moment]));
        _ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i + 1 < _ranges.Count;)
        {
            var a = _ranges[i]; var b = _ranges[i + 1];
            if (b.Start > a.End + MergeGapSeconds) { i++; continue; }
            _ranges[i] = new(a.Start, Math.Max(a.End, b.End), [..a.Moments, ..b.Moments]);
            _ranges.RemoveAt(i + 1);
        }
    }
    // Full-match ranges can be arbitrarily long. Replay saves flush incrementally so a
    // long fight cannot evict its beginning from a bounded in-memory replay buffer.
    public static bool ShouldSave(double now, ClipRange range, double oldestRetained, int bufferSeconds) =>
        now >= range.End || now - Math.Max(oldestRetained, range.Start) >= bufferSeconds - 4;
}
