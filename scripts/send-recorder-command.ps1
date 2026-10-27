param(
    [ValidateSet('status','arm','disarm','save','retry')][string]$Command = 'status',
    [string]$ProfileDirectory = (Join-Path $env:LOCALAPPDATA 'Echo')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProfileDirectory)
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))).Substring(0,16)
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.',"Echo-$hash",[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
try {
    $pipe.Connect(2000)
    $writer = [IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true)
    $writer.AutoFlush = $true
    $writer.WriteLine($Command)
    $reader = [IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,1024,$true)
    $line = $reader.ReadLine()
    if ($line) { $line | ConvertFrom-Json }
    $reader.Dispose()
    $writer.Dispose()
} finally { $pipe.Dispose() }
