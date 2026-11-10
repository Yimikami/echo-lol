using System.IO.Compression;
using System.Text.Json;

namespace Echo.Core;

public static class LibraryBackup
{
    private const string Catalog = "echo-library.json";
    private sealed record ArchiveCatalog(int Version, Session[] Sessions);

    public static void Save(string root, string destination, Func<bool> cancel)
    {
        var sessions = LibraryCatalog.Read(root).Where(s => s.EndedAt != null && !s.PreparationPending).ToArray();
        if (sessions.Length == 0) throw new InvalidOperationException("There are no completed recordings to back up.");
        if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a backup destination outside the recording folder.");
        var temporary = destination + $".{Guid.NewGuid():N}.partial";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var session in sessions)
                {
                    var folder = LibraryCatalog.Folder(root, session.Id);
                    var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    MapPaths(session, (path, extension) =>
                    {
                        if (names.TryGetValue(path, out var name)) return name;
                        var file = LibraryCatalog.Owned(folder, path);
                        var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                        if (fileExtension is not (".mp4" or ".mkv" or ".jpg" or ".jpeg" or ".png" or ".jsonl"))
                            fileExtension = extension;
                        name = $"asset-{names.Count}{fileExtension}";
                        names[path] = name;
                        if (File.Exists(file))
                        {
                            using var from = File.OpenRead(file);
                            using var to = zip.CreateEntry(Key(session.Id, name), CompressionLevel.NoCompression).Open();
                            Copy(from, to, cancel);
                        }
                        return name;
                    });
                    var poster = LibraryCatalog.Owned(folder, Path.Combine(folder, "match.jpg"));
                    if (File.Exists(poster))
                    {
                        using var from = File.OpenRead(poster);
                        using var to = zip.CreateEntry(Key(session.Id, "match.jpg"), CompressionLevel.NoCompression).Open();
                        Copy(from, to, cancel);
                    }
                    if (session.RecordingOptions is { } options)
                        session.RecordingOptions = options with { OutputDirectory = "", ObsDirectory = null };
                }
                using var manifest = zip.CreateEntry(Catalog).Open();
                JsonSerializer.Serialize(manifest, new ArchiveCatalog(1, sessions), JsonStore.Options);
            }
            if (cancel()) throw new OperationCanceledException();
            File.Move(temporary, destination, true);
        }
        finally { File.Delete(temporary); }
    }

    public static int Restore(string root, string source, Func<bool> cancel)
    {
        using var zip = ZipFile.OpenRead(source);
        var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        if (!entries.TryGetValue(Catalog, out var manifest) || manifest.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("This is not a supported Echo backup.");
        ArchiveCatalog? data;
        using (var stream = manifest.Open()) data = JsonSerializer.Deserialize<ArchiveCatalog>(stream, JsonStore.Options);
        if (data?.Version != 1 || data.Sessions.Length == 0 || data.Sessions.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Sessions.Length)
            throw new InvalidDataException("Invalid Echo backup manifest.");

        Directory.CreateDirectory(root);
        var stage = Path.Combine(root, $".restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        var folders = new List<(string Temporary, string Final)>();
        var published = new List<string>();
        try
        {
            foreach (var record in data.Sessions)
            {
                if (cancel()) throw new OperationCanceledException();
                if (record.EndedAt == null || record.PreparationPending) throw new InvalidDataException("The backup contains an unfinished recording.");
                ValidateComponent(record.Id);
                var session = record with { Id = Guid.NewGuid().ToString("N") };
                var final = LibraryCatalog.Folder(root, session.Id);
                var temporary = Path.Combine(stage, session.Id);
                Directory.CreateDirectory(temporary);
                folders.Add((temporary, final));
                var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MapPaths(session, (name, _) =>
                {
                    ValidateComponent(name);
                    if (!name.StartsWith("asset-", StringComparison.Ordinal) || name.Equals("session.json", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid asset in Echo backup.");
                    if (copied.Add(name)) Extract(name);
                    return Path.Combine(final, name);
                });
                Extract("match.jpg");
                if (session.RecordingOptions is { } options) session.RecordingOptions = options with { OutputDirectory = Path.GetFullPath(root), ObsDirectory = null };
                JsonStore.Write(Path.Combine(temporary, "session.json"), session);

                void Extract(string name)
                {
                    if (!entries.TryGetValue(Key(record.Id, name), out var entry)) return;
                    using var from = entry.Open();
                    using var to = File.Open(Path.Combine(temporary, name), FileMode.CreateNew, FileAccess.Write);
                    Copy(from, to, cancel);
                }
            }
            foreach (var (temporary, final) in folders)
            {
                if (cancel()) throw new OperationCanceledException();
                Directory.Move(temporary, final);
                published.Add(final);
            }
            return published.Count;
        }
        catch
        {
            foreach (var folder in published) Directory.Delete(folder, true);
            throw;
        }
        finally { Directory.Delete(stage, true); }
    }

    private static void MapPaths(Session session, Func<string, string, string> map)
    {
        if (session.RecordingPath is { } recording) session.RecordingPath = map(recording, ".mp4");
        if (session.InputLogPath is { } inputs) session.InputLogPath = map(inputs, ".jsonl");
        for (int i = 0; i < session.Clips.Count; i++)
        {
            var clip = session.Clips[i];
            session.Clips[i] = clip with { Path = string.IsNullOrWhiteSpace(clip.Path) ? "" : map(clip.Path, ".mp4"),
                ThumbnailPath = clip.ThumbnailPath is { } poster ? map(poster, ".jpg") : null };
        }
    }

    private static void ValidateComponent(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.EndsWith('.') || name.EndsWith(' '))
            throw new InvalidDataException("Invalid path in Echo backup.");
    }

    private static string Key(string session, string name) => $"sessions/{session}/{name}";

    private static void Copy(Stream from, Stream to, Func<bool> cancel)
    {
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = from.Read(buffer)) > 0)
        {
            if (cancel()) throw new OperationCanceledException();
            to.Write(buffer, 0, read);
        }
    }
}
