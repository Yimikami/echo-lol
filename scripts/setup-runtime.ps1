param(
    [string]$ObsDirectory = "$env:ProgramFiles\obs-studio",
    [string]$AppDirectory = (Join-Path $PSScriptRoot '../artifacts/app'),
    [switch]$BundleRuntime
)
$ErrorActionPreference = 'Stop'
$obsRoot = (Resolve-Path -LiteralPath $ObsDirectory).Path
$obsBin = Join-Path $obsRoot 'bin/64bit'
$obsDll = Join-Path $obsBin 'obs.dll'
if (!(Test-Path -LiteralPath $obsDll)) { throw 'Choose an extracted or installed OBS Studio 33.x directory.' }
$version = (Get-Item -LiteralPath $obsDll).VersionInfo.ProductVersion
if ($version -notmatch '^33\.') { throw "OBS $version is not validated. Use OBS Studio 33.x." }
$appRoot = [IO.Path]::GetFullPath($AppDirectory)
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
foreach ($helper in @('obs-nvenc-test.exe','obs-amf-test.exe','obs-qsv-test.exe','obs-ffmpeg-mux.exe')) {
    Copy-Item -LiteralPath (Join-Path $obsBin $helper) -Destination $appRoot -Force
}
if ($BundleRuntime) {
    $runtimeRoot = Join-Path $appRoot 'runtime'
    $runtimeBin = Join-Path $runtimeRoot 'bin/64bit'
    New-Item -ItemType Directory -Path $runtimeBin -Force | Out-Null
    # Select native media dependencies; do not include OBS Studio, Qt, browser, UI or PDBs.
    $patterns = @('obs.dll','libobs-*.dll','w32-pthreads.dll','avcodec-*.dll','avformat-*.dll','avutil-*.dll','swresample-*.dll','swscale-*.dll','avfilter-*.dll','avdevice-*.dll','libcurl.dll','zlib*.dll','libssl*.dll','libcrypto*.dll','librist.dll','libSRT.dll','srt.dll','libspeexdsp.dll','libopus*.dll','libvpx*.dll','libx264*.dll','libx265*.dll','vcruntime*.dll','msvcp*.dll','concrt*.dll','libmbed*.dll','libwinpthread*.dll','libiconv*.dll','libintl*.dll')
    foreach ($pattern in $patterns) { Get-ChildItem -LiteralPath $obsBin -Filter $pattern -File | Copy-Item -Destination $runtimeBin -Force }
    New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'data'),(Join-Path $runtimeRoot 'core') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $obsRoot 'data/libobs') -Destination (Join-Path $runtimeRoot 'data') -Recurse -Force
    foreach ($module in @('win-capture','win-wasapi','obs-ffmpeg','obs-nvenc','obs-qsv11','image-source')) {
        $moduleRoot = Join-Path $obsRoot "core/$module"
        if (!(Test-Path -LiteralPath $moduleRoot)) { throw 'This setup script currently expects the OBS 33 modern core/ module layout.' }
        $destination = Join-Path $runtimeRoot "core/$module"
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $moduleRoot -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $destination -Force
        if (Test-Path -LiteralPath (Join-Path $moduleRoot 'data')) { Copy-Item -LiteralPath (Join-Path $moduleRoot 'data') -Destination $destination -Recurse -Force }
    }
    $licenses = Join-Path $obsRoot 'data/obs-studio/license'
    if (Test-Path -LiteralPath $licenses) { Copy-Item -LiteralPath $licenses -Destination (Join-Path $runtimeRoot 'licenses') -Recurse -Force }
}
foreach ($tool in @('ffmpeg')) {
    $command = Get-Command "$tool.exe" -ErrorAction Stop
    $toolPath = $command.Source
    # Chocolatey shims are not portable. Resolve the actual installed media tools.
    if ($toolPath -like '*\chocolatey\bin\*') {
        $packages = Get-ChildItem -LiteralPath 'C:\ProgramData\chocolatey\lib' -Directory -Filter '*ffmpeg*'
        $candidate = $packages | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter "$tool.exe" -Recurse -File -ErrorAction SilentlyContinue } | Where-Object Length -gt 2MB | Select-Object -First 1
        if ($candidate) { $toolPath = $candidate.FullName } else { throw "Cannot resolve the real $tool executable behind the Chocolatey shim." }
    }
    $toolDirectory = Join-Path $appRoot 'tools'
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    Copy-Item -LiteralPath $toolPath -Destination (Join-Path $toolDirectory "$tool.exe") -Force
}
Write-Output "Runtime prepared from OBS $version. Installed OBS files were only read."
