param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (!$compiler) { $compiler = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 6/ISCC.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 is required to build the Windows installer.' }

$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$script = Join-Path $PSScriptRoot '../installer/Echo.iss'
& $compiler /Qp "/DEchoVersion=$Version" "/DPackageDirectory=$package" "/O$output" $script
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)." }

$installer = Join-Path $output "Echo-Setup-$Version-x64.exe"
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Setup executable was not produced.' }
if (!$SmokeTest) { return }

$target = Join-Path $env:RUNNER_TEMP "echo-installer-$([Guid]::NewGuid().ToString('N'))"
$log = Join-Path $env:RUNNER_TEMP 'echo-install.log'
$install = Start-Process -FilePath $installer -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', ('/DIR="{0}"' -f $target), ('/LOG="{0}"' -f $log)
) -WindowStyle Hidden -Wait -PassThru
if ($install.ExitCode -ne 0) { Get-Content -LiteralPath $log -Tail 40; throw "Setup smoke test failed ($($install.ExitCode))." }

foreach ($relative in @('Echo.exe', 'Echo.Recorder.exe', 'coreclr.dll', 'runtime/bin/64bit/obs.dll', 'tools/ffmpeg.exe', 'LICENSE', 'SOURCE-MANIFEST.json')) {
    $installed = Join-Path $target $relative
    if (!(Test-Path -LiteralPath $installed -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash) {
        throw "Installed file is missing or does not match: $relative"
    }
}

$uninstaller = Get-ChildItem -LiteralPath $target -Filter 'unins*.exe' -File | Select-Object -First 1
if (!$uninstaller) { throw 'Setup did not install an uninstaller.' }
$uninstall = Start-Process -FilePath $uninstaller.FullName -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $target 'Echo.exe'))) {
    throw "Uninstall smoke test failed ($($uninstall.ExitCode))."
}
Write-Output 'Setup and uninstall smoke tests passed.'
