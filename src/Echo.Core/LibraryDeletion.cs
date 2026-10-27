namespace Echo.Core;

public static class LibraryDeletion
{
    /// <summary>Updates one completed session. The caller supplies the platform's recycle operation.</summary>
    public static bool Remove(string libraryRoot, string sessionId, string clipId, bool wholeMatch, Action<string> recycle)
    {
        var root = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var folder = Path.GetFullPath(Path.Combine(root, sessionId));
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The recording is outside the library.");
        var metadata = Path.Combine(folder, "session.json");
        var latest = JsonStore.Read<Session>(metadata) ?? throw new IOException("The recording catalog could not be opened.");
        if (latest.EndedAt == null) throw new InvalidOperationException("Wait until this recording has finished.");
        var clip = wholeMatch ? null : latest.Clips.SingleOrDefault(c => c.Id == clipId) ?? throw new IOException("This clip has already been removed.");
        var path = wholeMatch ? latest.RecordingPath ?? throw new IOException("The match has already been removed.") : clip!.Path;
        var removed = latest.Clips.Where(c => wholeMatch ? string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase) : c.Id == clipId).ToArray();
        bool shouldRecycle = wholeMatch || !clip!.IsVirtual && !latest.Clips.Except(removed).Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)) && !string.Equals(latest.RecordingPath, path, StringComparison.OrdinalIgnoreCase);
        var media = Path.GetFullPath(path);
        if (shouldRecycle && !media.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Echo only deletes video files inside their recording folder.");
        var original = JsonStore.Read<Session>(metadata)!;
        latest.Clips.RemoveAll(c => removed.Any(r => r.Id == c.Id));
        if (wholeMatch) latest.RecordingPath = null;
        JsonStore.Write(metadata, latest);
        try { if (shouldRecycle && File.Exists(media)) recycle(media); }
        catch { JsonStore.Write(metadata, original); throw; }
        return shouldRecycle;
    }
}
