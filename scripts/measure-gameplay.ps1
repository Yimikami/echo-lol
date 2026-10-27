param(
    [Parameter(Mandatory)][string]$PresentMonPath,
    [Parameter(Mandatory)][ValidateSet('baseline','clips','full-match')][string]$Label,
    [int]$Seconds = 30,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/benchmarks')
)
$ErrorActionPreference = 'Stop'
if ($Seconds -lt 10 -or $Seconds -gt 300) { throw 'Use a 10–300 second sample.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$target = Get-Process -Name 'League of Legends' -ErrorAction Stop | Select-Object -First 1
$output = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "$Label-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')).csv"
$sessionName = 'EchoBench-' + [guid]::NewGuid().ToString('N').Substring(0,8)
Write-Output "Measuring $Label for $Seconds seconds. Keep the same scene, camera and game settings."
& $PresentMonPath --process_id $target.Id --output_file $output --timed $Seconds --terminate_after_timed --no_console_stats --no_track_input --session_name $sessionName
if ($LASTEXITCODE -ne 0) { throw 'PresentMon could not collect the sample. Check its permissions; Echo does not change Windows security settings.' }
Write-Output "Saved $output"
