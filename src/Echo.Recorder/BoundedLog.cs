using System.Text;

namespace Echo.Recorder;

/// <summary>The recorder owns its log; closing the UI never closes recorder output pipes.</summary>
internal sealed class BoundedLog : TextWriter
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    public override Encoding Encoding => Encoding.UTF8;
    public BoundedLog(string path) => _writer = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    public override void WriteLine(string? value)
    {
        try
        {
            lock (_gate)
            {
                if (_writer.BaseStream.Length > 8 * 1024 * 1024) { _writer.Flush(); _writer.BaseStream.SetLength(0); }
                _writer.WriteLine(value);
            }
        }
        catch (IOException) { /* Logging must never terminate a recording. */ }
        catch (ObjectDisposedException) { }
    }
    protected override void Dispose(bool disposing) { if (disposing) { lock (_gate) _writer.Dispose(); } base.Dispose(disposing); }
}
