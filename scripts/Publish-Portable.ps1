[CmdletBinding()]
param(
    [string]$DotNetPath = 'D:\Program Files\dotnet\dotnet.exe',
    [string]$PresentMonPath = 'D:\Program Files\GameNest\PresentMon\2.5.1\PresentMon-2.5.1-x64.exe',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw -Encoding UTF8
    $Version = $buildProps.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be MAJOR.MINOR.PATCH: $Version"
}
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
$packageName = "GameNest-$Version-win-x64-portable"
$packageRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactRoot "$packageName.zip"))
$hashPath = [IO.Path]::GetFullPath((Join-Path $artifactRoot "$packageName.sha256"))
$projectPath = Join-Path $repositoryRoot 'src\GameNest.App\GameNest.App.csproj'
$appBuildOutput = Join-Path $repositoryRoot 'src\GameNest.App\bin\Release\net10.0-windows10.0.19041.0\win-x64'
$overlayOutput = Join-Path $repositoryRoot 'src\GameNest.Overlay\bin\Release\net10.0-windows10.0.19041.0\win-x64'

function Assert-PathWithinRepositoryArtifacts([string]$PathToCheck) {
    $fullPath = [IO.Path]::GetFullPath($PathToCheck)
    $prefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the artifacts directory: $fullPath"
    }
}

if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "Pinned .NET SDK was not found: $DotNetPath"
}
if (-not (Test-Path -LiteralPath $PresentMonPath -PathType Leaf)) {
    throw "PresentMon 2.5.1 was not found: $PresentMonPath"
}
$expectedPresentMonHash = '9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191'
$actualPresentMonHash = (Get-FileHash -LiteralPath $PresentMonPath -Algorithm SHA256).Hash
if ($actualPresentMonHash -ne $expectedPresentMonHash) {
    throw "PresentMon hash mismatch: $actualPresentMonHash"
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Assert-PathWithinRepositoryArtifacts $packageRoot
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}

& $DotNetPath publish $projectPath -c Release --no-restore -r win-x64 --self-contained true `
    -o $packageRoot /p:PublishSingleFile=false /p:DebugType=None /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw 'GameNest.App Release publish failed.'
}

foreach ($winUiResource in @('App.xbf', 'MainWindow.xbf', 'GameNest.App.pri')) {
    $resourcePath = Join-Path $appBuildOutput $winUiResource
    if (-not (Test-Path -LiteralPath $resourcePath -PathType Leaf)) {
        throw "WinUI application resource was not found: $resourcePath"
    }
    Copy-Item -LiteralPath $resourcePath -Destination $packageRoot -Force
}

if (-not (Test-Path -LiteralPath (Join-Path $overlayOutput 'GameNest.Overlay.exe'))) {
    throw "Overlay Release output was not found: $overlayOutput"
}
$packageOverlay = Join-Path $packageRoot 'Overlay'
New-Item -ItemType Directory -Path $packageOverlay -Force | Out-Null
Copy-Item -Path (Join-Path $overlayOutput '*') -Destination $packageOverlay -Recurse -Force

$packagePresentMon = Join-Path $packageRoot 'Tools\PresentMon'
New-Item -ItemType Directory -Path $packagePresentMon -Force | Out-Null
Copy-Item -LiteralPath $PresentMonPath -Destination (Join-Path $packagePresentMon 'PresentMon-2.5.1-x64.exe')

$licenseDirectory = Join-Path $packageRoot 'LICENSES'
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\licenses\PresentMon-2.5.1-LICENSE.txt') -Destination $licenseDirectory
$packageThirdPartyDirectory = Join-Path $packageRoot 'docs\third-party'
$packageDocsLicenseDirectory = Join-Path $packageRoot 'docs\licenses'
New-Item -ItemType Directory -Path $packageThirdPartyDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packageDocsLicenseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\third-party\PresentMon-2.5.1.md') -Destination $packageThirdPartyDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\licenses\PresentMon-2.5.1-LICENSE.txt') -Destination $packageDocsLicenseDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\user-guide.md') -Destination (Join-Path $packageRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\privacy.md') -Destination (Join-Path $packageRoot 'PRIVACY.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\overlay-compatibility.md') -Destination (Join-Path $packageRoot 'OVERLAY-COMPATIBILITY.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\Uninstall-GameNest.cmd') -Destination $packageRoot

$uninstallSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Uninstall-GameNest.ps1') -Raw -Encoding UTF8
[IO.File]::WriteAllText(
    (Join-Path $packageRoot 'Uninstall-GameNest.ps1'),
    $uninstallSource,
    [Text.UTF8Encoding]::new($true))

[IO.File]::WriteAllText(
    (Join-Path $packageRoot '.gamenest-portable-root'),
    "GameNest portable root`r`n",
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $packageRoot 'VERSION.txt'),
    "GameNest $Version`r`nRuntime: .NET 10 self-contained x64`r`nWindows App SDK: 2.3.1`r`nPresentMon: 2.5.1`r`n",
    [Text.UTF8Encoding]::new($false))

Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.ilk') } |
    Remove-Item -Force

& (Join-Path $repositoryRoot 'scripts\Verify-ReleasePackage.ps1') -PackageDirectory $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Release audit failed.'
}

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText(
    $hashPath,
    "$zipHash  $([IO.Path]::GetFileName($zipPath))`r`n",
    [Text.UTF8Encoding]::new($false))

Write-Host "Portable directory: $packageRoot"
Write-Host "Portable archive: $zipPath"
Write-Host "SHA256 file: $hashPath"
Write-Host "SHA256: $zipHash"
