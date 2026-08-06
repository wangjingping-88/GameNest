[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw -Encoding UTF8
    $Version = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
}
$artifactRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageFile = Join-Path $artifactRoot "GameNest-$Version-win-x64-portable.zip"
if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
    throw "Portable ZIP must be generated before the update release test: $packageFile"
}

$testKeyRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("GameNest-Phase7-" + [Guid]::NewGuid().ToString('N'))))
$tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $testKeyRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create test keys outside the temporary directory: $testKeyRoot"
}
New-Item -ItemType Directory -Path $testKeyRoot | Out-Null
$privateKeyFile = Join-Path $testKeyRoot 'private.base64'
$publicKeyFile = Join-Path $testKeyRoot 'public.base64'
$oldPrivateKey = [Environment]::GetEnvironmentVariable('GAMENEST_UPDATE_PRIVATE_KEY')
try {
    & (Join-Path $PSScriptRoot 'New-UpdateSigningKey.ps1') `
        -PrivateKeyOutput $privateKeyFile `
        -PublicKeyOutput $publicKeyFile
    if ($LASTEXITCODE -ne 0) {
        throw 'Ephemeral update key generation failed.'
    }
    [Environment]::SetEnvironmentVariable(
        'GAMENEST_UPDATE_PRIVATE_KEY',
        (Get-Content -LiteralPath $privateKeyFile -Raw -Encoding UTF8).Trim())
    & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') `
        -PackageFile $packageFile `
        -Version $Version `
        -KeyId 'phase7-ephemeral-test' `
        -PublishedAtUtc ([DateTimeOffset]::UtcNow)
    if ($LASTEXITCODE -ne 0) {
        throw 'Ephemeral update manifest signing failed.'
    }
    & (Join-Path $PSScriptRoot 'Audit-ReleaseAssets.ps1') `
        -ArtifactDirectory $artifactRoot `
        -Version $Version `
        -PublicKeyBase64 ((Get-Content -LiteralPath $publicKeyFile -Raw -Encoding UTF8).Trim())
    if ($LASTEXITCODE -ne 0) {
        throw 'Ephemeral release asset audit failed.'
    }
}
finally {
    [Environment]::SetEnvironmentVariable('GAMENEST_UPDATE_PRIVATE_KEY', $oldPrivateKey)
    $resolvedTestKeyRoot = [IO.Path]::GetFullPath($testKeyRoot)
    if ($resolvedTestKeyRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestKeyRoot -PathType Container)) {
        Remove-Item -LiteralPath $resolvedTestKeyRoot -Recurse -Force
    }
}

Write-Host 'Ephemeral signature and release asset audit passed. These test assets must not be published.'
