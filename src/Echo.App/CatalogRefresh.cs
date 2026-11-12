using System.IO;
using System.Text.Json;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private void RefreshCatalog(bool force = false)
    {
        if (_deleteCandidate != null || _libraryBusy) return;
        try
        {
            string[] folders;
            try { folders = Directory.GetDirectories(_settings.OutputDirectory); }
            catch (DirectoryNotFoundException) { _items.Clear(); _catalogSignature = ""; ApplyFilter(); ClearObservedError("catalog"); return; }
            var files = folders.Where(dir => !Path.GetFileName(dir).StartsWith('.'))
                .SelectMany(dir => Directory.EnumerateFiles(dir, "session.json", SearchOption.TopDirectoryOnly))
                .Select(path => new FileInfo(path)).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
            var queue = Path.Combine(_paths.Root, "preparation");
            var jobs = Directory.Exists(queue) ? Directory.GetFiles(queue)
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json.failed", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path)).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray() : [];
            var signature = string.Join(";", files.Concat(jobs).Select(f => $"{f.FullName}:{f.LastWriteTimeUtc.Ticks}:{f.Length}"));
            if (!force && signature == _catalogSignature) { ClearObservedError("catalog"); return; }

            var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var finalized = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in jobs)
            {
                if (JsonStore.Read<PreparationJob>(file.FullName) is not { } job) continue;
                if (job.FinalizedSession is { } snapshot) finalized[job.Metadata] = snapshot;
                if (file.Name.EndsWith(".failed", StringComparison.OrdinalIgnoreCase))
                    failed[job.Metadata] = job.Error ?? "Preparation was interrupted. Try preparing this recording again.";
                else pending.Add(job.Metadata);
            }

            // Publish a new snapshot only after every read succeeds. A locked file must not
            // erase the gallery or cache its timestamp before the next refresh can retry it.
            var items = new List<ClipItem>();
            var failures = new List<Session>();
            foreach (var file in files)
            {
                var session = JsonStore.Read<Session>(file.FullName)
                    ?? throw new IOException($"Recording metadata changed while reading '{file.FullName}'. Try again.");
                if (session.Id != file.Directory!.Name) continue;
                if (finalized.TryGetValue(file.FullName, out var final) && final.Id == session.Id) session = final;
                if (pending.Contains(file.FullName))
                {
                    session.PreparationPending = true;
                    if (session.Error?.StartsWith("Clip preparation", StringComparison.Ordinal) == true) session.Error = null;
                }
                else if (failed.TryGetValue(file.FullName, out var error))
                {
                    session.PreparationPending = false;
                    session.Error = "Clip preparation needs attention: " + error;
                }
                if (session.DeletedAt == null && session.Error?.StartsWith("Clip preparation", StringComparison.Ordinal) == true) failures.Add(session);
                bool trash = _libraryView == 2;
                if (session.DeletedAt != null)
                {
                    if (trash) items.Add(MatchItem(session, file.DirectoryName!));
                    continue;
                }
                if (_matches && !trash)
                {
                    if (session.Mode == RecordingMode.FullMatch && session.RecordingPath != null)
                    {
                        var match = MatchItem(session, file.DirectoryName!);
                        if (ShowEntry(match)) items.Add(match);
                    }
                    continue;
                }
                if (session.EndedAt == null) continue;
                foreach (var clip in session.Clips)
                {
                    var item = new ClipItem(session, clip, clip.ThumbnailPath ?? Path.Combine(file.DirectoryName!, clip.Id + ".jpg"));
                    if (trash ? clip.DeletedAt != null : clip.DeletedAt == null && ShowEntry(item)) items.Add(item);
                }
            }
            items.Sort((a, b) => b.Session.StartedAt.CompareTo(a.Session.StartedAt));
            _items.Clear(); _items.AddRange(items); _catalogSignature = signature;
            ApplyFilter();
            ClearObservedError("catalog");
            var activeFailures = failures.Select(s => "preparation:" + s.Id).ToHashSet();
            foreach (var source in _observedErrors.Keys.Where(k => k.StartsWith("preparation:") && !activeFailures.Contains(k)).ToArray()) ClearObservedError(source);
            foreach (var session in failures) ShowPreparationError(session);
            ScheduleStorageCleanup();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            ObserveError("catalog", "Couldn't refresh library", error.ToString(), "Try again", () => RefreshCatalog(true));
        }
    }
}
