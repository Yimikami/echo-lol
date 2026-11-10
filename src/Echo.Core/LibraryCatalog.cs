namespace Echo.Core;

public readonly record struct LibraryEntry(string SessionId, string ClipId, bool WholeMatch);
public readonly record struct LibraryUsage(long ActiveBytes, long TrashBytes);

public static class LibraryCatalog
{
    private static readonly object Gate = new();
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public static string Folder(string root, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            id.Contains('/') || id.Contains('\\') || id.Contains(':') || id.EndsWith('.') || id.EndsWith(' '))
            throw new InvalidDataException("Invalid recording ID.");
        var folder = Path.GetFullPath(Path.Combine(root, id));
        if (Directory.Exists(folder) && File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Linked recording folders cannot be modified.");
        return folder;
    }

    internal static string Owned(string folder, string path)
    {
        var full = Path.GetFullPath(path);
        if (!Paths.Equals(Path.GetDirectoryName(full), folder) ||
            (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)))
            throw new IOException("This file is outside the managed recording folder.");
        return full;
    }

    public static IReadOnlyList<Session> Read(string root)
    {
        if (!Directory.Exists(root)) return [];
        var sessions = new List<Session>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Path.GetFileName(folder).StartsWith('.')) continue;
                var id = Path.GetFileName(folder);
                var file = Owned(Folder(root, id), Path.Combine(folder, "session.json"));
                if (JsonStore.Read<Session>(file) is { } session && session.Id == id)
                    sessions.Add(session);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return sessions;
    }

    private static Session Completed(string metadata, string id)
    {
        var session = JsonStore.Read<Session>(metadata) ?? throw new IOException("The recording catalog could not be opened.");
        if (session.Id != id) throw new InvalidDataException("The recording ID does not match its folder.");
        if (session.EndedAt == null || session.PreparationPending) throw new InvalidOperationException("Wait until this recording has finished preparing.");
        return session;
    }

    public static void Edit(string root, IEnumerable<LibraryEntry> entries, Func<Session, MediaClip?, MediaClip?> edit)
    {
        lock (Gate)
        {
            var updates = entries.Distinct().GroupBy(entry => entry.SessionId).Select(group =>
            {
                var folder = Folder(root, group.Key);
                var metadata = Owned(folder, Path.Combine(folder, "session.json"));
                var session = Completed(metadata, group.Key);
                foreach (var entry in group)
                {
                    if (entry.WholeMatch) { edit(session, null); continue; }
                    int index = session.Clips.FindIndex(clip => clip.Id == entry.ClipId);
                    if (index < 0) throw new IOException("This clip is no longer in the library.");
                    session.Clips[index] = edit(session, session.Clips[index]) ?? throw new InvalidOperationException("The clip edit returned no result.");
                }
                return (metadata, session);
            }).ToArray();
            foreach (var (metadata, session) in updates) JsonStore.Write(metadata, session);
        }
    }

    public static void SetTrash(string root, IEnumerable<LibraryEntry> entries, bool deleted)
    {
        DateTimeOffset? date = deleted ? DateTimeOffset.UtcNow : null;
        Edit(root, entries, (session, clip) =>
        {
            if (clip != null) return clip with { DeletedAt = date };
            session.DeletedAt = date;
            return null;
        });
    }

    private static IEnumerable<(string Path, bool Active)> References(Session session)
    {
        if (session.RecordingPath is { } video) yield return (video, session.DeletedAt == null);
        foreach (var clip in session.Clips)
            if (!string.IsNullOrWhiteSpace(clip.Path))
                yield return (clip.Path, session.DeletedAt == null && clip.DeletedAt == null);
    }

    private static IEnumerable<(string Path, bool Active)> ManagedMedia(string root, Session session)
    {
        var folder = Folder(root, session.Id);
        var paths = new Dictionary<string, bool>(Paths);
        foreach (var (path, active) in References(session))
        {
            try
            {
                var file = Owned(folder, path);
                if (File.Exists(file)) paths[file] = active || paths.GetValueOrDefault(file);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException) { }
        }
        return paths.Select(file => (file.Key, file.Value));
    }

    public static LibraryUsage Usage(string root)
    {
        long active = 0, trash = 0;
        foreach (var group in Read(root).SelectMany(session => ManagedMedia(root, session)).GroupBy(file => file.Path, Paths))
        {
            var info = new FileInfo(group.Key);
            if (!info.Exists) continue;
            if (group.Any(file => file.Active)) active += info.Length;
            else trash += info.Length;
        }
        return new(active, trash);
    }

    public static int Manage(string root, long limit, Action<string> remove, Func<bool> cancel)
    {
        lock (Gate)
        {
            if (limit <= 0 || cancel()) return 0;
            long used = Usage(root).ActiveBytes;
            var candidates = Read(root).Where(s => s.EndedAt != null && !s.PreparationPending && s.DeletedAt == null)
                .OrderBy(s => s.StartedAt);
            var count = 0;
            foreach (var session in candidates)
            {
                if (used <= limit || cancel()) break;
                if (session.Favorite || session.Clips.Any(c => c.Favorite)) continue;
                var media = ManagedMedia(root, session).Where(m => m.Active).ToArray();
                if (media.Length == 0) continue;
                var bytes = media.Sum(m => new FileInfo(m.Path).Length);
                SetTrash(root, [new(session.Id, session.Id, true)], true);
                Purge(root, session.Id, remove);
                used -= bytes;
                count++;
            }
            return count;
        }
    }

    public static void Purge(string root, string id, Action<string> remove)
    {
        lock (Gate)
        {
            var folder = Folder(root, id);
            var metadata = Owned(folder, Path.Combine(folder, "session.json"));
            var session = Completed(metadata, id);
            foreach (var (file, active) in ManagedMedia(root, session))
                if (!active) remove(file);
            if (session.DeletedAt != null)
            {
                DeleteSidecar(session.InputLogPath);
                foreach (var clip in session.Clips) DeleteSidecar(clip.ThumbnailPath);
                DeleteSidecar(Path.Combine(folder, "match.jpg"));
                File.Delete(metadata);
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            else
            {
                var usedPosters = session.Clips.Where(c => c.DeletedAt == null && c.ThumbnailPath != null)
                    .Select(c => c.ThumbnailPath!).ToHashSet(Paths);
                if (session.RecordingPath != null) usedPosters.Add(Path.Combine(folder, "match.jpg"));
                foreach (var clip in session.Clips.Where(c => c.DeletedAt != null))
                    if (clip.ThumbnailPath != null && !usedPosters.Contains(clip.ThumbnailPath)) DeleteSidecar(clip.ThumbnailPath);
                session.Clips.RemoveAll(c => c.DeletedAt != null);
                JsonStore.Write(metadata, session);
            }

            void DeleteSidecar(string? path)
            {
                if (path == null || !File.Exists(path)) return;
                if (References(session).Any(r => Paths.Equals(r.Path, path))) return;
                File.Delete(Owned(folder, path));
            }
        }
    }

    public static Session Import(string root, string source, double duration, Action<string, string> poster, Func<bool> cancel)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Choose a video with a readable duration.");
        var session = new Session { Mode = RecordingMode.FullMatch, State = "Completed", EndedAt = DateTimeOffset.UtcNow,
            Champion = "Imported video", Title = Path.GetFileNameWithoutExtension(source), Duration = duration };
        var folder = Folder(root, session.Id);
        var staging = Path.Combine(Path.GetDirectoryName(folder)!, $".import-{session.Id}");
        Directory.CreateDirectory(staging);
        try
        {
            var video = Path.Combine(staging, "match.mp4");
            CopyVideo(source, video, cancel);
            poster(video, Path.Combine(staging, "match.jpg"));
            session.RecordingPath = Path.Combine(folder, "match.mp4");
            JsonStore.Write(Path.Combine(staging, "session.json"), session);
            if (cancel()) throw new OperationCanceledException();
            Directory.Move(staging, folder);
            return session;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static void Relink(string root, LibraryEntry entry, string source, double duration, Func<bool> cancel)
    {
        lock (Gate)
        {
            var folder = Folder(root, entry.SessionId);
            var metadata = Owned(folder, Path.Combine(folder, "session.json"));
            var session = Completed(metadata, entry.SessionId);
            var path = entry.WholeMatch ? session.RecordingPath : session.Clips.SingleOrDefault(c => c.Id == entry.ClipId)?.Path;
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("This entry has no video to relink.");
            if (File.Exists(path)) throw new IOException("The original video is already available.");
            var shared = session.Clips.Where(c => Paths.Equals(c.Path, path)).ToArray();
            var originalMatch = Paths.Equals(session.RecordingPath, path);
            double needed = Math.Max(originalMatch ? session.Duration : 0, shared.Select(c => c.End).DefaultIfEmpty().Max());
            if (!double.IsFinite(duration) || duration <= 0 || duration + .25 < needed)
                throw new InvalidDataException("This video is shorter than the footage referenced in Echo.");
            var destination = Path.Combine(folder, $"relinked-{Guid.NewGuid():N}.mp4");
            try
            {
                CopyVideo(source, destination, cancel);
                if (originalMatch) session.RecordingPath = destination;
                for (int i = 0; i < session.Clips.Count; i++)
                    if (Paths.Equals(session.Clips[i].Path, path))
                        session.Clips[i] = session.Clips[i] with { Path = destination, VideoUnavailable = false };
                JsonStore.Write(metadata, session);
            }
            catch { File.Delete(destination); throw; }
        }
    }

    private static void CopyVideo(string source, string destination, Func<bool> cancel)
    {
        using var from = File.OpenRead(source);
        using var to = File.Open(destination, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = from.Read(buffer)) > 0)
        {
            if (cancel()) throw new OperationCanceledException();
            to.Write(buffer, 0, read);
        }
    }
}
