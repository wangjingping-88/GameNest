[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageFile,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$KeyId,

    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$PublishedAtUtc,

    [int]$MinimumOsBuild = 19041,

    [string]$PrivateKeyEnvironmentVariable = 'GAMENEST_UPDATE_PRIVATE_KEY',

    [string]$DotNetPath = 'D:\Program Files\dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$packagePath = [IO.Path]::GetFullPath($PackageFile)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Update package does not exist: $packagePath"
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be MAJOR.MINOR.PATCH: $Version"
}
if ([string]::IsNullOrWhiteSpace($KeyId)) {
    throw 'KeyId must not be empty.'
}

$packageBaseName = "GameNest-$Version-win-x64-portable"
$expectedName = "$packageBaseName.zip"
if ([IO.Path]::GetFileName($packagePath) -cne $expectedName) {
    throw "Unexpected update package name. Expected $expectedName"
}

$privateKeyBase64 = [Environment]::GetEnvironmentVariable($PrivateKeyEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($privateKeyBase64)) {
    throw "Signing secret is missing: $PrivateKeyEnvironmentVariable"
}

$package = Get-Item -LiteralPath $packagePath
$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    channel = 'stable'
    rid = 'win-x64'
    assetName = $expectedName
    size = [long]$package.Length
    sha256 = $sha256
    minimumOsBuild = $MinimumOsBuild
    publishedAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    keyId = $KeyId
}
$manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Compress))
$manifestPath = Join-Path $package.DirectoryName "$packageBaseName.update.json"
$signaturePath = Join-Path $package.DirectoryName "$packageBaseName.update.sig"

[IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "Pinned .NET SDK was not found: $DotNetPath"
}
& $DotNetPath run --file (Join-Path $PSScriptRoot 'UpdateCryptoTool.cs') -- sign $manifestPath $signaturePath
$signExitCode = $LASTEXITCODE
$privateKeyBase64 = $null
if ($signExitCode -ne 0) {
    throw "Update manifest signing failed with exit code $signExitCode."
}

Write-Host "Update manifest: $manifestPath"
Write-Host "Update signature: $signaturePath"
