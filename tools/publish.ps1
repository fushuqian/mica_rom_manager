# Publish the portable single-file build and verify the output before declaring success.
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$AllowRunning
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'dotnet not found' }

if (-not $AllowRunning -and (Get-Process -Name 'MicaRomManager' -ErrorAction SilentlyContinue)) {
    throw 'MicaRomManager is running. Close it before publishing (or pass -AllowRunning on a copy).'
}

& $dotnet publish (Join-Path $root 'src\MicaRomManager\MicaRomManager.csproj') `
    -c $Configuration -r $Runtime --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

& (Join-Path $PSScriptRoot 'verify-publish.ps1') -PublishDir $publishDir
if ($LASTEXITCODE -ne 0) { throw 'publish output verification failed' }

Write-Host 'publish finished and verified.'
