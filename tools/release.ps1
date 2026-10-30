# Build the release zip from publish/, take notes from CHANGELOG, create the GitHub release.
# Keep this file ASCII-only: Windows PowerShell reads BOM-less .ps1 as ANSI and mangles non-ASCII.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$SkipPublish,
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$releaseDir = Join-Path $env:TEMP 'mica-release'

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$zip = Join-Path $releaseDir "MicaRomManager-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($rel in @('MicaRomManager.exe', 'Resources\EmulatorTemplates.json', 'Resources\DeviceProfiles.json')) {
        $source = Join-Path $publishDir $rel
        if (-not (Test-Path -LiteralPath $source)) { throw "missing publish file: $source" }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $source, ($rel -replace '\\', '/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }

# Take the matching CHANGELOG section as the release note.
$pattern = '^## .*v' + [regex]::Escape($Version) + '$'
$lines = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$startIndex = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $pattern) { $startIndex = $i; break }
}
if ($startIndex -lt 0) { throw "CHANGELOG has no section for version $Version" }

$endIndex = $lines.Count
for ($i = $startIndex + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $endIndex = $i; break }
}
$body = ($lines[$startIndex..($endIndex - 1)] -join "`n").Trim()
$notes = Join-Path $releaseDir "notes-v$Version.md"
[System.IO.File]::WriteAllText($notes, $body, [System.Text.UTF8Encoding]::new($false))

# Keep a local copy (publish/ is gitignored).
Copy-Item -LiteralPath $zip -Destination $publishDir -Force

$ghArgs = @('release', 'create', "v$Version", $zip, '--title', "v$Version", '--notes-file', $notes)
if ($Draft) { $ghArgs += '--draft' } else { $ghArgs += '--latest' }
& gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE" }

Write-Host "release v$Version published: $zip"
