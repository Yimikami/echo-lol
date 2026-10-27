param([string]$OutputDirectory='artifacts/release')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
try {
    $dependencies=Join-Path $repo 'artifacts/release-dependencies'
    $output=[IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
    New-Item -ItemType Directory -Force -Path $dependencies,$output | Out-Null
    function Download-Verified([string]$url,[string]$path,[string]$hash) {
        if(!(Test-Path -LiteralPath $path)){Invoke-WebRequest -Uri $url -OutFile $path}
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash){throw "Checksum mismatch: $url"}
    }
    $obsArchive=Join-Path $dependencies 'obs-33.0.0.zip'
    Download-Verified 'https://github.com/obsproject/obs-studio/releases/download/33.0.0/OBS-Studio-33.0.0-Windows-x64.zip' $obsArchive '3369a3ecd67b5669a8a08d77357f87cc786d1fba42fbbd1054d841a69f6271a7'
    $obs=Join-Path $dependencies 'obs'
    if(!(Test-Path (Join-Path $obs 'bin/64bit/obs.dll'))){Expand-Archive -LiteralPath $obsArchive -DestinationPath $obs -Force}
    $ffArchive=Join-Path $dependencies 'ffmpeg-9.0.2.7z'
    Download-Verified 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.7z' $ffArchive '4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077'
    $ffRoot=Join-Path $dependencies 'ffmpeg'
    if(!(Test-Path $ffRoot)){& 7z x $ffArchive "-o$ffRoot" -y | Out-Null;if($LASTEXITCODE-ne0){throw 'FFmpeg extraction failed'}}
    $ffmpeg=Get-ChildItem -LiteralPath $ffRoot -Filter ffmpeg.exe -Recurse -File | Select-Object -First 1
    if(!$ffmpeg){throw 'FFmpeg executable missing'}
    $env:PATH=$ffmpeg.DirectoryName+[IO.Path]::PathSeparator+$env:PATH
    & "$PSScriptRoot/build.ps1" -BundleRuntime -SelfContained -ObsDirectory $obs -OutputDirectory 'artifacts/portable-release'
    if($LASTEXITCODE-ne0){throw 'Portable build failed'}
    $package=Join-Path $repo 'artifacts/portable-release'
    $licenses=Join-Path $package 'licenses/FFmpeg';New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Get-ChildItem -LiteralPath $ffmpeg.Directory.Parent.FullName -File | Copy-Item -Destination $licenses -Force
    & $ffmpeg.FullName -version 2>&1 | Set-Content (Join-Path $licenses 'build-configuration.txt')
    $sha=(& git rev-parse HEAD).Trim()
    $manifest=@{
        echo=@{revision=$sha;source="https://github.com/Yimikami/echo-lol/tree/$sha";license='GPL-3.0-or-later'}
        obs=@{version='33.0.0';archive='https://github.com/obsproject/obs-studio/releases/download/33.0.0/OBS-Studio-33.0.0-Windows-x64.zip';sha256=(Get-FileHash $obsArchive).Hash;source='https://github.com/obsproject/obs-studio/releases/download/33.0.0/OBS-Studio-33.0.0-Sources.tar.gz';build='https://github.com/obsproject/obs-studio/tree/33.0.0/.github/workflows'}
        ffmpeg=@{version='9.0.2';archive='https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.7z';sha256=(Get-FileHash $ffArchive).Hash;source='https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz';builder='https://www.gyan.dev/ffmpeg/builds/';configuration='licenses/FFmpeg/build-configuration.txt'}
        dotnet=@{runtime='Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.x';source='https://github.com/dotnet/dotnet';license='MIT'}
    }
    $manifest|ConvertTo-Json -Depth 6|Set-Content (Join-Path $package 'SOURCE-MANIFEST.json')
    $obsSource=Join-Path $output 'OBS-33.0.0-Sources.tar.gz'
    Download-Verified 'https://github.com/obsproject/obs-studio/releases/download/33.0.0/OBS-Studio-33.0.0-Sources.tar.gz' $obsSource 'c1ae3c98c1bb70ebdced33e894a526fcbbbda9bfd95806970a3fc31f74b08f6c'
    $ffSource=Join-Path $output 'FFmpeg-9.0.2-Sources.tar.xz'
    if(!(Test-Path $ffSource)){Invoke-WebRequest 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' -OutFile $ffSource}
    $archive=Join-Path $output 'Echo-Windows-x64.zip'
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -Force
    Copy-Item (Join-Path $package 'SOURCE-MANIFEST.json') -Destination $output -Force
    Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$_.Name } | Set-Content (Join-Path $output 'SHA256SUMS.txt')
} finally {Pop-Location}
