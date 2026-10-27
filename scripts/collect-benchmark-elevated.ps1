param([Parameter(Mandatory)][string]$PresentMonPath,[Parameter(Mandatory)][string]$SessionDirectory,[int]$ProcessId)
$ErrorActionPreference = 'Stop'
$labels = @('baseline','clips','baseline','full-match')
try {
    for ($stage = 0; $stage -lt $labels.Count; $stage++) {
        $ready = Join-Path $SessionDirectory "ready-$stage.json"
        $deadline = [DateTime]::UtcNow.AddMinutes(4)
        while (!(Test-Path -LiteralPath $ready)) {
            if (Test-Path -LiteralPath (Join-Path $SessionDirectory 'abort.flag')) { exit 1 }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Timed out waiting for the normal-user recording controller.' }
            Start-Sleep -Milliseconds 500
        }
        $csv = Join-Path $SessionDirectory "$stage-$($labels[$stage]).csv"
        $info = [Diagnostics.ProcessStartInfo]::new($PresentMonPath)
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($arg in @('--process_id',"$ProcessId",'--output_file',$csv,'--timed','30','--terminate_after_timed','--no_console_stats','--no_track_input','--session_name',"EchoBench-$([guid]::NewGuid().ToString('N').Substring(0,8))")) { $info.ArgumentList.Add($arg) }
        $capture = [Diagnostics.Process]::Start($info)
        $stdout = $capture.StandardOutput.ReadToEndAsync(); $stderr = $capture.StandardError.ReadToEndAsync()
        if (!$capture.WaitForExit(50000)) { $capture.Kill($true); throw 'PresentMon timed out.' }
        ($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()) | Set-Content -LiteralPath (Join-Path $SessionDirectory "$stage-trace.log")
        if ($capture.ExitCode -ne 0) { throw "PresentMon stage $stage failed with exit $($capture.ExitCode)." }
        @{stage=$stage;label=$labels[$stage];completedAt=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $SessionDirectory "done-$stage.json")
        $capture.Dispose()
    }
} catch { $_.Exception.Message | Set-Content -LiteralPath (Join-Path $SessionDirectory 'error.txt'); exit 1 }
