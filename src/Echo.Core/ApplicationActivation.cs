using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Echo.Core;

public sealed class ApplicationActivation : IDisposable
{
    private const byte OpenWindow = 1;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _name;

    private ApplicationActivation(Mutex mutex, string name) { _mutex = mutex; _name = name; }

    private static string Name(string? scope)
    {
        using var process = Process.GetCurrentProcess();
        var identity = $"{Environment.UserDomainName}/{Environment.UserName}/{process.SessionId}/{scope}";
        return "Echo-Window-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    public static ApplicationActivation? TryAcquire(string? scope = null)
    {
        var name = Name(scope);
        var mutex = new Mutex(false, @"Local\" + name);
        try
        {
            if (mutex.WaitOne(0)) return new(mutex, name);
        }
        catch (AbandonedMutexException) { return new(mutex, name); }
        mutex.Dispose();
        return null;
    }

    public async Task ListenAsync(Action activate)
    {
        var cancellation = _stop.Token;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                request.CancelAfter(TimeSpan.FromSeconds(1));
                var command = new byte[1];
                if (await pipe.ReadAsync(command, request.Token).ConfigureAwait(false) == 1 && command[0] == OpenWindow)
                    activate();
            }
            catch (OperationCanceledException) { }
            catch (IOException)
            {
                if (cancellation.IsCancellationRequested) return;
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    public static async Task<bool> TryActivateAsync(int timeoutMilliseconds, string? scope = null)
    {
        using var pipe = new NamedPipeClientStream(".", Name(scope), PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
                AllowSetForegroundWindow(processId);
            await pipe.WriteAsync(new byte[] { OpenWindow }, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
