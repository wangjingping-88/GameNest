[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyOutput,

    [Parameter(Mandatory = $true)]
    [string]$PublicKeyOutput,

    [string]$DotNetPath = 'D:\Program Files\dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$privatePath = [IO.Path]::GetFullPath($PrivateKeyOutput)
$publicPath = [IO.Path]::GetFullPath($PublicKeyOutput)
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($privatePath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The production private key must be written outside the GameNest repository.'
}

foreach ($path in @($privatePath, $publicPath)) {
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite an existing key file: $path"
    }
}

if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "Pinned .NET SDK was not found: $DotNetPath"
}
& $DotNetPath run --file (Join-Path $PSScriptRoot 'UpdateCryptoTool.cs') -- generate $privatePath $publicPath
if ($LASTEXITCODE -ne 0) {
    throw "ECDSA P-256 key generation failed with exit code $LASTEXITCODE."
}

Write-Host "ECDSA P-256 key generated. Private key path: $privatePath"
Write-Host "Public key path: $publicPath"
Write-Host 'Store the private-key value in GitHub Secret GAMENEST_UPDATE_PRIVATE_KEY. Never commit or log it.'
