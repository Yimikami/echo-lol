param([switch]$BundleRuntime, [switch]$SelfContained, [string]$ObsDirectory = "$env:ProgramFiles\obs-studio", [string]$OutputDirectory = 'artifacts/app')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $appDirectory = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    dotnet build Echo.slnx -c Release --artifacts-path artifacts/build --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/Echo.Tests -c Release --artifacts-path artifacts/build --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    foreach ($project in @('Echo.App','Echo.Recorder')) {
        if($SelfContained){dotnet publish "src/$project/$project.csproj" -c Release --artifacts-path artifacts/build -o $appDirectory --self-contained true -r win-x64 --nologo}
        else{dotnet publish "src/$project/$project.csproj" -c Release --artifacts-path artifacts/build -o $appDirectory --self-contained false --nologo}
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    & "$PSScriptRoot/setup-runtime.ps1" -ObsDirectory $ObsDirectory -AppDirectory $appDirectory -BundleRuntime:$BundleRuntime
    Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md -Destination $appDirectory -Force
    Copy-Item -LiteralPath scripts/Launch-Echo.cmd -Destination $appDirectory -Force
    if (Test-Path -LiteralPath assets/licenses) { Copy-Item -LiteralPath assets/licenses -Destination $appDirectory -Recurse -Force }
    Write-Output "Echo is ready at $appDirectory\Echo.exe"
} finally { Pop-Location }
