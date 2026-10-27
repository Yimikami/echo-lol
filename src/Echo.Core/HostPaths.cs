using System.Security.Cryptography;
using System.Text;

namespace Echo.Core;

public sealed class HostPaths
{
    public string Root { get; }
    public string Settings => Path.Combine(Root, "settings.json");
    public string Status => Path.Combine(Root, "status.json");
    public string PipeName => "Echo-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))[..16];
    public HostPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echo"));
        Directory.CreateDirectory(Root);
    }
    public EchoSettings LoadSettings() { var settings = JsonStore.Read<EchoSettings>(Settings) ?? new(); settings.Validate(); return settings; }
}
