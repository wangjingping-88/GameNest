[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "Release directory does not exist: $packageRoot"
}
$packageDirectoryName = [IO.Path]::GetFileName($packageRoot)
if ($packageDirectoryName -notmatch '^GameNest-(\d+\.\d+\.\d+)-win-x64-portable$') {
    throw "Release directory name does not match the fixed package contract: $packageDirectoryName"
}
$expectedVersion = $Matches[1]

$requiredFiles = @(
    'GameNest.App.exe',
    'App.xbf',
    'MainWindow.xbf',
    'GameNest.App.pri',
    'Assets\Brand\GameNest.ico',
    'Assets\Brand\GameNest_App_Icon.png',
    'Assets\Brand\GameNest_Logo_Horizontal.png',
    'Overlay\GameNest.Overlay.exe',
    'Tools\PresentMon\PresentMon-2.5.1-x64.exe',
    'README.md',
    'PRIVACY.md',
    'OVERLAY-COMPATIBILITY.md',
    'THIRD_PARTY_NOTICES.md',
    'LICENSES\PresentMon-2.5.1-LICENSE.txt',
    'Uninstall-GameNest.ps1',
    'Uninstall-GameNest.cmd',
    '.gamenest-portable-root',
    'VERSION.txt'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $packageRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
}

$versionText = Get-Content -LiteralPath (Join-Path $packageRoot 'VERSION.txt') -Raw -Encoding UTF8
if (-not $versionText.StartsWith("GameNest $expectedVersion`r`n", [StringComparison]::Ordinal)) {
    throw "VERSION.txt does not match package version $expectedVersion"
}
$appVersion = (Get-Item -LiteralPath (Join-Path $packageRoot 'GameNest.App.exe')).VersionInfo
if ($appVersion.FileVersion -cne "$expectedVersion.0" -or
    -not $appVersion.ProductVersion.StartsWith($expectedVersion, [StringComparison]::Ordinal)) {
    throw "GameNest.App.exe version does not match package version $expectedVersion"
}

$debugArtifacts = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.ilk') }
if ($debugArtifacts) {
    throw "Debug artifacts were found: $($debugArtifacts.FullName -join ', ')"
}

$presentMonPath = Join-Path $packageRoot 'Tools\PresentMon\PresentMon-2.5.1-x64.exe'
$expectedPresentMonHash = '9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191'
$actualPresentMonHash = (Get-FileHash -LiteralPath $presentMonPath -Algorithm SHA256).Hash
if ($actualPresentMonHash -ne $expectedPresentMonHash) {
    throw "PresentMon hash mismatch: $actualPresentMonHash"
}

$textExtensions = @('.config', '.cmd', '.json', '.md', '.ps1', '.txt', '.xml')
$forbiddenPatterns = @(
    'D:\\code\\GameNest',
    'C:\\Users\\',
    '(?i)test[_-]?api[_-]?key',
    '(?i)sk-test-[A-Za-z0-9_-]+',
    'BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY'
)
$textFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Extension -in $textExtensions }

foreach ($textFile in $textFiles) {
    $content = Get-Content -LiteralPath $textFile.FullName -Raw -Encoding UTF8
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            throw "Forbidden release content ($pattern): $($textFile.FullName)"
        }
    }
}

$gameNestBinaries = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Name -like 'GameNest.*.dll' -or $_.Name -like 'GameNest.*.exe' }
$forbiddenBinaryStrings = @('D:\code\GameNest', 'C:\Users\')
$latinEncoding = [Text.Encoding]::GetEncoding(28591)
foreach ($binary in $gameNestBinaries) {
    $bytes = [IO.File]::ReadAllBytes($binary.FullName)
    $latinText = $latinEncoding.GetString($bytes)
    foreach ($forbidden in $forbiddenBinaryStrings) {
        if ($latinText.IndexOf($forbidden, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "A GameNest binary contains an absolute development path: $($binary.FullName)"
        }
    }
}

Write-Host "Release audit passed: $packageRoot"
