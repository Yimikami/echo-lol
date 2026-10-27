param([Parameter(Mandatory)][string]$PresentMonPath,[Parameter(Mandatory)][string]$ProfileDirectory,[Parameter(Mandatory)][string]$SessionDirectory)
$ErrorActionPreference = 'Stop'
$profileRoot = [IO.Path]::GetFullPath($ProfileDirectory)
$sessionRoot = [IO.Path]::GetFullPath($SessionDirectory)
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
$target = Get-Process -Name 'League of Legends' -ErrorAction Stop | Select-Object -First 1
$labels = @('baseline','clips','baseline','full-match')
$settingsPath = Join-Path $profileRoot 'settings.json'
$originalSettings = Get-Content -LiteralPath $settingsPath -Raw
$collector = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'collect-benchmark-elevated.ps1'))
$powershell = (Get-Command pwsh.exe).Source
# The elevated process only traces ETW. Recorder control stays at its original elevation.
# Windows user-only named pipes also distinguish elevation levels.
$info = [Diagnostics.ProcessStartInfo]::new($powershell)
$info.UseShellExecute = $true; $info.Verb = 'runas'; $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
foreach ($arg in @('-NoProfile','-File',$collector,'-PresentMonPath',[IO.Path]::GetFullPath($PresentMonPath),'-SessionDirectory',$sessionRoot,'-ProcessId',"$($target.Id)")) { $info.ArgumentList.Add($arg) }
$admin = [Diagnostics.Process]::Start($info)
function Status { Get-Content -LiteralPath (Join-Path $profileRoot 'status.json') -Raw | ConvertFrom-Json }
function Control([string]$command) { $null = & "$PSScriptRoot/send-recorder-command.ps1" -Command $command -ProfileDirectory $profileRoot }
function Wait-Recording([bool]$wanted) {
    $limit = [DateTime]::UtcNow.AddSeconds(60)
    $requestedAt = [DateTime]::UtcNow
    do {
        $status = Status
        $fresh = [DateTime]::Parse($status.updatedAt).ToUniversalTime() -ge $requestedAt
        if ($fresh -and $status.error -and $wanted -and !$status.recording) { throw $status.error }
        if ($fresh -and $status.armed -eq $wanted -and $status.recording -eq $wanted -and $status.state -ne 'Finalizing recording' -and (!$wanted -or $status.state -eq 'Recording')) { return }
        if ([DateTime]::UtcNow -gt $limit) { throw 'Recorder did not reach the requested state. Keep the game foreground and inspect status.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
}
try {
    Write-Output 'Windows approval requested. After approving, return to League and keep the same quiet camera view.'
    Start-Sleep -Seconds 10
    for ($stage=0; $stage -lt $labels.Count; $stage++) {
        $label = $labels[$stage]
        Control 'disarm'; Wait-Recording $false
        if ($label -ne 'baseline') {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            $settings.mode = if ($label -eq 'full-match') { 'FullMatch' } else { 'Clips' }
            $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$settingsPath.bench-tmp"
            Move-Item -LiteralPath "$settingsPath.bench-tmp" -Destination $settingsPath -Force
            Control 'retry'; Wait-Recording $true
            Start-Sleep -Seconds 10
        } else { Start-Sleep -Seconds 5 }
        $focusDeadline = [DateTime]::UtcNow.AddMinutes(2)
        while ((Status).gameForeground -ne $true) {
            if ([DateTime]::UtcNow -gt $focusDeadline) { throw 'Game was not foreground. Return to the game before collecting a comparable sample.' }
            Start-Sleep -Seconds 1
        }
        Start-Sleep -Seconds 3
        Write-Output "STAGE $stage $label — 30 seconds"
        @{stage=$stage;label=$label;startedAt=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionRoot "ready-$stage.json")
        $samples = [Collections.Generic.List[object]]::new()
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while (!(Test-Path -LiteralPath (Join-Path $sessionRoot "done-$stage.json"))) {
            if (Test-Path -LiteralPath (Join-Path $sessionRoot 'error.txt')) { throw (Get-Content -LiteralPath (Join-Path $sessionRoot 'error.txt') -Raw) }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Trace stage timed out.' }
            $samples.Add((Status))
            Start-Sleep -Seconds 1
        }
        $samples | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $sessionRoot "$stage-recorder.json")
        Write-Output "DONE $stage $label"
    }
    @{completed=$true;at=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionRoot 'complete.json')
} finally {
    if (!(Test-Path -LiteralPath (Join-Path $sessionRoot 'complete.json'))) { 'abort' | Set-Content -LiteralPath (Join-Path $sessionRoot 'abort.flag') }
    Control 'disarm'; Wait-Recording $false
    $originalSettings | Set-Content -LiteralPath "$settingsPath.bench-tmp"
    Move-Item -LiteralPath "$settingsPath.bench-tmp" -Destination $settingsPath -Force
    Control 'retry'
    $admin.Dispose()
}
