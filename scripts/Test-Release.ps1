[CmdletBinding()]
param(
    [string]$DotNetPath = 'D:\Program Files\dotnet\dotnet.exe',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'GameNest.sln'

if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "Pinned .NET SDK was not found: $DotNetPath"
}

if (-not $NoBuild) {
    & $DotNetPath build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed.'
    }
}

$testProjects = @(
    'tests\GameNest.Domain.Tests\GameNest.Domain.Tests.csproj',
    'tests\GameNest.Application.Tests\GameNest.Application.Tests.csproj',
    'tests\GameNest.Infrastructure.Tests\GameNest.Infrastructure.Tests.csproj',
    'tests\GameNest.Telemetry.Tests\GameNest.Telemetry.Tests.csproj'
)

foreach ($testProject in $testProjects) {
    $projectPath = Join-Path $repositoryRoot $testProject
    & $DotNetPath test $projectPath -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Test project failed: $testProject"
    }
}

Write-Host 'Release build and all test projects passed.'
