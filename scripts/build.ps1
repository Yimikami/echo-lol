param([switch]$BundleRuntime, [string]$ObsDirectory = "$env:ProgramFiles\obs-studio")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    dotnet build Echo.slnx -c Release --artifacts-path artifacts/build --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/Echo.Tests -c Release --artifacts-path artifacts/build --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    foreach ($project in @('Echo.App','Echo.Recorder')) {
        dotnet publish "src/$project/$project.csproj" -c Release --artifacts-path artifacts/build -o artifacts/app --self-contained false --nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    & "$PSScriptRoot/setup-runtime.ps1" -ObsDirectory $ObsDirectory -AppDirectory (Join-Path $root 'artifacts/app') -BundleRuntime:$BundleRuntime
    Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md -Destination artifacts/app -Force
    Copy-Item -LiteralPath scripts/Launch-Echo.cmd -Destination artifacts/app -Force
    Write-Output "Echo is ready at $root\artifacts\app\Echo.exe"
} finally { Pop-Location }
