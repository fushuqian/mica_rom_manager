# Verify that a publish output is complete and matches the current source.
# Fails (exit 1) on any problem so it can be used as a release gate.
param(
    [string]$PublishDir = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishDir)) { $PublishDir = Join-Path $root 'publish' }

$problems = @()
$exe = Join-Path $PublishDir 'MicaRomManager.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    $problems += "missing exe: $exe"
}

foreach ($name in @('EmulatorTemplates.json', 'DeviceProfiles.json')) {
    $path = Join-Path $PublishDir "Resources\$name"
    if (-not (Test-Path -LiteralPath $path)) {
        $problems += "missing resource: $path"
    }
}

$csproj = Join-Path $root 'src\MicaRomManager\MicaRomManager.csproj'
$version = $null
if (Test-Path -LiteralPath $csproj) {
    $match = Select-String -LiteralPath $csproj -Pattern '<Version>([^<]+)</Version>'
    if ($match) { $version = $match.Matches[0].Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($version)) {
    $problems += "cannot read <Version> from $csproj"
}

$head = ''
try { $head = (& git -C $root rev-parse --short HEAD 2>$null).Trim() } catch { $head = '' }

if (Test-Path -LiteralPath $exe) {
    $info = (Get-Item -LiteralPath $exe).VersionInfo
    $fileVersion = "$($info.FileVersion)".Trim()

    if ($version) {
        $expected = if ($fileVersion.Split('.').Count -ge 4) { "$version.0" } else { $version }
        if ($fileVersion -ne $expected -and $fileVersion -ne $version) {
            $problems += "exe FileVersion '$fileVersion' != csproj Version '$version'"
        }
    }

    if ($head) {
        $product = "$($info.ProductVersion)"
        if ($product -notmatch [regex]::Escape($head)) {
            $problems += "exe ProductVersion '$product' does not contain current HEAD '$head' (stale build)"
        }
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "publish output is NOT releasable: $PublishDir"
    exit 1
}

Write-Host "publish output OK: version $version, HEAD $head, dir $PublishDir"
exit 0
