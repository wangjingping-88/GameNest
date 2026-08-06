[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublicKeyBase64,

    [string]$DotNetPath = 'D:\Program Files\dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$baseName = "GameNest-$Version-win-x64-portable"
$zipPath = Join-Path $artifactRoot "$baseName.zip"
$hashPath = Join-Path $artifactRoot "$baseName.sha256"
$manifestPath = Join-Path $artifactRoot "$baseName.update.json"
$signaturePath = Join-Path $artifactRoot "$baseName.update.sig"
foreach ($path in @($zipPath, $hashPath, $manifestPath, $signaturePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release asset is missing: $path"
    }
}

$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
$manifest = [Text.UTF8Encoding]::new($false).GetString($manifestBytes) | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or
    $manifest.version -cne $Version -or
    $manifest.channel -cne 'stable' -or
    $manifest.rid -cne 'win-x64' -or
    $manifest.assetName -cne "$baseName.zip" -or
    $manifest.minimumOsBuild -lt 19041) {
    throw 'Update manifest fields do not match the release contract.'
}

$zip = Get-Item -LiteralPath $zipPath
$actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
if ([long]$manifest.size -ne [long]$zip.Length -or $manifest.sha256 -cne $actualHash) {
    throw 'Update manifest size or SHA-256 does not match the ZIP asset.'
}
$hashLine = (Get-Content -LiteralPath $hashPath -Raw -Encoding UTF8).Trim()
if ($hashLine -cne "$actualHash  $baseName.zip") {
    throw 'SHA-256 companion asset has unexpected content.'
}

$oldPublicKey = [Environment]::GetEnvironmentVariable('GAMENEST_UPDATE_PUBLIC_KEY')
try {
    [Environment]::SetEnvironmentVariable('GAMENEST_UPDATE_PUBLIC_KEY', $PublicKeyBase64)
    & $DotNetPath run --file (Join-Path $PSScriptRoot 'UpdateCryptoTool.cs') -- verify $manifestPath $signaturePath
    if ($LASTEXITCODE -ne 0) {
        throw "Update manifest signature verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('GAMENEST_UPDATE_PUBLIC_KEY', $oldPublicKey)
}

$forbidden = @('BEGIN PRIVATE KEY', 'BEGIN EC PRIVATE KEY', 'D:\code\GameNest', 'C:\Users\')
foreach ($textAsset in @($hashPath, $manifestPath)) {
    $content = Get-Content -LiteralPath $textAsset -Raw -Encoding UTF8
    foreach ($pattern in $forbidden) {
        if ($content.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Release asset contains forbidden content: $textAsset"
        }
    }
}

Write-Host "Release asset audit passed: $baseName"
