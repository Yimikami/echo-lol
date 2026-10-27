namespace Echo.Core;

public sealed record ExitRequest(string Id, DateTimeOffset RequestedAt);
public static class ApplicationExit
{
    public static string FilePath(HostPaths paths) => Path.Combine(paths.Root, "exit-request.json");
    public static string? Current(HostPaths paths)
    {
        try { return JsonStore.Read<ExitRequest>(FilePath(paths))?.Id; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }
    public static void Request(HostPaths paths)
    {
        JsonStore.Write(FilePath(paths), new ExitRequest(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
        File.WriteAllText(paths.StopRequest, "stop");
    }
}
